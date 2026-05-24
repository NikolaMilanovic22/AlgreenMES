using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetDeliveryCompliance;
using AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProcessTimeTrend;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Modules.Production.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Orders.Infrastructure.Services;

public class ReportingQueryService : IReportingQueryService
{
    private readonly OrdersDbContext _ordersDb;
    private readonly ProductionDbContext _productionDb;
    private readonly IdentityDbContext _identityDb;

    public ReportingQueryService(
        OrdersDbContext ordersDb,
        ProductionDbContext productionDb,
        IdentityDbContext identityDb)
    {
        _ordersDb = ordersDb;
        _productionDb = productionDb;
        _identityDb = identityDb;
    }

    public async Task<ProcessTimesDto> GetProcessTimesAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        List<Guid>? productCategoryIds,
        List<OrderType>? orderTypes,
        CancellationToken cancellationToken = default)
    {
        var processes = await _productionDb.Processes
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .OrderBy(p => p.SequenceOrder)
            .Select(p => new { p.Id, p.Code, p.Name })
            .ToListAsync(cancellationToken);

        // IsExcludedFromReports rows are filtered out at the source — Vremena
        // is an aggregate view and excluded samples must not influence stats.
        var query = _ordersDb.OrderItemProcesses
            .AsNoTracking()
            .Include(p => p.OrderItem)
                .ThenInclude(oi => oi.Order)
            .Include(p => p.SubProcesses)
            .Where(p => p.TenantId == tenantId
                && p.Status == ProcessStatus.Completed
                && p.Complexity.HasValue
                && p.TotalDurationMinutes > 0
                && !p.IsExcludedFromReports);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(p => p.CompletedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(p => p.CompletedAt <= toUtc);
        }

        if (productCategoryIds is { Count: > 0 })
            query = query.Where(p => productCategoryIds.Contains(p.OrderItem.ProductCategoryId));

        if (orderTypes is { Count: > 0 })
            query = query.Where(p => orderTypes.Contains(p.OrderItem.Order.OrderType));

        // TotalDurationMinutes column actually stores seconds (misnamed).
        // If a process has sub-processes, its effective duration is the sum
        // of sub-process durations — the parent column is wall-clock from
        // Start to Complete and includes idle gaps between sub-process
        // activations, which Sale/Bojan don't want counted (see feedback
        // 22.05.2026: ORD-2026-025 E-STAKLO parent 0:06:56 vs subs 0:03:21).
        var entities = await query.ToListAsync(cancellationToken);

        var grouped = entities
            .Select(p => new
            {
                p.ProcessId,
                p.Complexity,
                Seconds = EffectiveDurationSeconds(p),
            })
            .Where(r => r.Seconds > 0)
            .GroupBy(r => new { r.ProcessId, r.Complexity })
            .ToDictionary(g => g.Key, g => ComputeStats(g.Select(x => x.Seconds / 60.0).ToList()));

        var result = new List<ProcessTimeItemDto>();
        foreach (var process in processes)
        {
            var stats = new Dictionary<string, ComplexityStatsDto>();
            foreach (var complexity in Enum.GetValues<ComplexityType>())
            {
                var key = new { ProcessId = process.Id, Complexity = (ComplexityType?)complexity };
                if (grouped.TryGetValue(key, out var s))
                    stats[complexity.ToString()] = s;
            }
            result.Add(new ProcessTimeItemDto(process.Id, process.Code, process.Name, stats));
        }

        return new ProcessTimesDto(result);
    }

    /// <summary>
    /// Effective duration for reporting purposes — in the misnamed-seconds
    /// "TotalDurationMinutes" unit.
    ///
    /// If a process has any sub-processes with non-zero duration, the
    /// effective duration is the SUM of those sub-process durations rather
    /// than the parent's wall-clock value. The parent's TotalDurationMinutes
    /// counts every second between Start and Complete (incl. idle gaps
    /// between sub-process activations), but Sale/Bojan want only the active
    /// sub-process work to count (feedback 22.05.2026: ORD-2026-025 E-STAKLO
    /// parent column showed 0:06:56 vs sub-process sum 0:03:21).
    ///
    /// If the process has no sub-processes (single-timer path), the parent
    /// column already reflects only active timer time, so use it as-is.
    /// </summary>
    private static int EffectiveDurationSeconds(
        AlGreenMES.Modules.Orders.Domain.Entities.OrderItemProcess process)
    {
        var subSum = process.SubProcesses
            .Where(sp => !sp.IsWithdrawn && sp.TotalDurationMinutes > 0)
            .Sum(sp => sp.TotalDurationMinutes);
        return subSum > 0 ? subSum : process.TotalDurationMinutes;
    }

    // Stats math extracted to ReportingStats (public, unit-testable).
    private static ComplexityStatsDto ComputeStats(List<double> values) =>
        ReportingStats.ComputeStats(values);

    public async Task<TimeTrackingReportDto> GetTimeTrackingReportAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        Guid? processId,
        ComplexityType? complexity,
        string? orderNumber,
        List<Guid>? productCategoryIds,
        List<OrderType>? orderTypes,
        CancellationToken cancellationToken = default)
    {
        var processes = await _productionDb.Processes
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToDictionaryAsync(p => p.Id, p => new { p.Code, p.Name }, cancellationToken);

        var categoryNames = await _productionDb.ProductCategories
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId)
            .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        var subProcessNames = await _productionDb.SubProcesses
            .AsNoTracking()
            .Where(sp => sp.TenantId == tenantId)
            .ToDictionaryAsync(sp => sp.Id, sp => sp.Name, cancellationToken);

        var query = _ordersDb.OrderItemProcesses
            .AsNoTracking()
            .Include(p => p.OrderItem)
                .ThenInclude(oi => oi.Order)
            .Include(p => p.SubProcesses)
            .Where(p => p.TenantId == tenantId
                && p.Status == ProcessStatus.Completed
                && p.TotalDurationMinutes > 0);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(p => p.CompletedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(p => p.CompletedAt <= toUtc);
        }

        if (processId.HasValue)
            query = query.Where(p => p.ProcessId == processId.Value);

        if (complexity.HasValue)
            query = query.Where(p => p.Complexity == complexity.Value);

        if (!string.IsNullOrWhiteSpace(orderNumber))
        {
            var pattern = $"%{orderNumber.Trim()}%";
            query = query.Where(p => EF.Functions.ILike(p.OrderItem.Order.OrderNumber, pattern));
        }

        if (productCategoryIds is { Count: > 0 })
            query = query.Where(p => productCategoryIds.Contains(p.OrderItem.ProductCategoryId));

        if (orderTypes is { Count: > 0 })
            query = query.Where(p => orderTypes.Contains(p.OrderItem.Order.OrderType));

        var data = await query
            .OrderByDescending(p => p.CompletedAt)
            .ToListAsync(cancellationToken);

        var items = data.Select(p =>
        {
            var proc = processes.GetValueOrDefault(p.ProcessId);
            var categoryName = categoryNames.GetValueOrDefault(p.OrderItem?.ProductCategoryId ?? Guid.Empty, "—");
            var subs = p.SubProcesses
                .Where(sp => sp.TotalDurationMinutes > 0)
                .Select(sp => new SubProcessTimeDto(
                    sp.SubProcessId,
                    subProcessNames.GetValueOrDefault(sp.SubProcessId, "Unknown"),
                    sp.TotalDurationMinutes))
                .ToList();

            return new TimeTrackingItemDto(
                p.Id,
                p.OrderItem?.Order?.OrderNumber ?? "—",
                categoryName,
                p.OrderItem?.Order?.OrderType.ToString() ?? "—",
                p.ProcessId,
                proc?.Code ?? "?",
                proc?.Name ?? "Unknown",
                p.Complexity?.ToString(),
                p.Status.ToString(),
                p.StartedAt,
                p.CompletedAt,
                EffectiveDurationSeconds(p),
                p.IsExcludedFromReports,
                subs);
        }).ToList();

        return new TimeTrackingReportDto(items);
    }

    public async Task<WorkerHoursReportDto> GetWorkerHoursReportAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        var sessionsQuery = _ordersDb.WorkSessions
            .AsNoTracking()
            .Where(ws => ws.TenantId == tenantId
                && ws.Date >= from
                && ws.Date <= to
                && ws.CheckOutTime.HasValue
                && ws.DurationMinutes.HasValue);

        if (userId.HasValue)
            sessionsQuery = sessionsQuery.Where(ws => ws.UserId == userId.Value);

        var sessions = await sessionsQuery.ToListAsync(cancellationToken);

        var userIds = sessions.Select(s => s.UserId).Distinct().ToList();
        var users = await _identityDb.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}", cancellationToken);

        var grouped = sessions
            .GroupBy(s => s.UserId)
            .Select(g =>
            {
                var dailyBreakdown = g
                    .GroupBy(s => s.Date)
                    .OrderBy(d => d.Key)
                    .Select(d => new WorkerHoursDayDto(
                        d.Key,
                        d.Sum(s => s.DurationMinutes ?? 0),
                        d.Count()))
                    .ToList();

                return new WorkerHoursSummaryDto(
                    g.Key,
                    users.GetValueOrDefault(g.Key, "Unknown"),
                    g.Sum(s => s.DurationMinutes ?? 0),
                    g.Count(),
                    dailyBreakdown);
            })
            .OrderBy(w => w.FullName)
            .ToList();

        return new WorkerHoursReportDto(grouped);
    }

    public async Task<DeliveryComplianceReportDto> GetDeliveryComplianceAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        ReportGranularity granularity,
        List<OrderType>? orderTypes,
        CancellationToken cancellationToken = default)
    {
        var query = _ordersDb.Orders
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId
                && o.CompletedAt.HasValue);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(o => o.CompletedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(o => o.CompletedAt <= toUtc);
        }

        if (orderTypes is { Count: > 0 })
            query = query.Where(o => orderTypes.Contains(o.OrderType));

        var orders = await query
            .Select(o => new { o.CompletedAt, o.DeliveryDate })
            .ToListAsync(cancellationToken);

        // Bucket by ISO week (Monday start) or month — match the granularity
        // the FE picker selects. "On-time" = CompletedAt date ≤ DeliveryDate date
        // (day-precision; we don't compare timestamps within a day because
        // delivery dates have wall-clock-of-day semantics, not exact times).
        var grouped = orders
            .Where(o => o.CompletedAt.HasValue)
            .GroupBy(o => BucketStart(o.CompletedAt!.Value, granularity))
            .Select(g => new DeliveryComplianceBucketDto(
                g.Key,
                g.Count(o => o.CompletedAt!.Value.Date <= o.DeliveryDate.Date),
                g.Count(o => o.CompletedAt!.Value.Date > o.DeliveryDate.Date)))
            .OrderBy(b => b.BucketStart)
            .ToList();

        return new DeliveryComplianceReportDto(grouped);
    }

    private static DateTime BucketStart(DateTime d, ReportGranularity granularity)
    {
        if (granularity == ReportGranularity.Month)
        {
            return DateTime.SpecifyKind(new DateTime(d.Year, d.Month, 1), DateTimeKind.Utc);
        }
        // ISO week: Monday is day-1. C# DayOfWeek puts Sunday=0, Monday=1.
        var dayOffset = ((int)d.DayOfWeek + 6) % 7; // Monday=0, Sunday=6
        return DateTime.SpecifyKind(d.Date.AddDays(-dayOffset), DateTimeKind.Utc);
    }

    public async Task<ProcessTimeTrendDto> GetProcessTimeTrendAsync(
        Guid tenantId,
        Guid processId,
        ComplexityType complexity,
        ReportGranularity granularity,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        // Same filters as GetProcessTimes — completed processes for the
        // single chosen process+complexity, excluding manually-isključi rows.
        var query = _ordersDb.OrderItemProcesses
            .AsNoTracking()
            .Include(p => p.SubProcesses)
            .Where(p => p.TenantId == tenantId
                && p.ProcessId == processId
                && p.Complexity == complexity
                && p.Status == ProcessStatus.Completed
                && p.TotalDurationMinutes > 0
                && !p.IsExcludedFromReports
                && p.CompletedAt.HasValue);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(p => p.CompletedAt >= fromUtc);
        }

        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(p => p.CompletedAt <= toUtc);
        }

        var entities = await query.ToListAsync(cancellationToken);

        if (entities.Count == 0)
        {
            return new ProcessTimeTrendDto(new List<ProcessTimeTrendBucketDto>(), null);
        }

        // Compute per-bucket stats (window-clamped min/max + trimmed mean) so
        // the green zone in the chart matches the Vremena table semantics.
        var buckets = entities
            .Select(p => new { p.CompletedAt, Minutes = EffectiveDurationSeconds(p) / 60.0 })
            .GroupBy(x => BucketStart(x.CompletedAt!.Value, granularity))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var values = g.Select(x => x.Minutes).ToList();
                var stats = ComputeStats(values);
                return new ProcessTimeTrendBucketDto(
                    g.Key,
                    stats.Count,
                    stats.TrimmedMeanMinutes,
                    stats.MinMinutes,
                    stats.MaxMinutes);
            })
            .ToList();

        // Normativ = 85% of trimmed mean across ALL filtered samples (not
        // bucket-aware) so it's a single constant target line.
        var overallValues = entities
            .Select(p => EffectiveDurationSeconds(p) / 60.0)
            .ToList();
        var overallStats = ComputeStats(overallValues);
        var normativ = Math.Round(overallStats.TrimmedMeanMinutes * 0.85, 2);

        return new ProcessTimeTrendDto(buckets, normativ);
    }

    public async Task<ActiveProcessFunnelDto> GetActiveProcessFunnelAsync(
        Guid tenantId,
        List<OrderType>? orderTypes,
        ComplexityType? complexity,
        CancellationToken cancellationToken = default)
    {
        var processes = await _productionDb.Processes
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .OrderBy(p => p.SequenceOrder)
            .Select(p => new { p.Id, p.Code, p.Name, p.SequenceOrder })
            .ToListAsync(cancellationToken);

        // Active OrderItemProcesses — InProgress / Pending / Blocked, not withdrawn.
        // We include sibling processes (.Processes on OrderItem) so we can
        // evaluate "ready" (all dependencies complete-or-withdrawn) without
        // a second roundtrip per row.
        //
        // AsNoTrackingWithIdentityResolution because the Include chain
        // OrderItemProcess → OrderItem → Processes creates a cycle back to
        // OrderItemProcess. Plain AsNoTracking refuses cycles (EF throws);
        // this variant resolves shared instances by identity without
        // tracking them for change detection.
        var query = _ordersDb.OrderItemProcesses
            .AsNoTrackingWithIdentityResolution()
            .Include(p => p.OrderItem)
                .ThenInclude(oi => oi.Processes)
            .Include(p => p.OrderItem)
                .ThenInclude(oi => oi.Order)
            .Where(p => p.TenantId == tenantId
                && !p.IsWithdrawn
                && (p.Status == ProcessStatus.InProgress
                    || p.Status == ProcessStatus.Pending
                    || p.Status == ProcessStatus.Blocked));

        if (orderTypes is { Count: > 0 })
            query = query.Where(p => orderTypes.Contains(p.OrderItem.Order.OrderType));
        if (complexity.HasValue)
            query = query.Where(p => p.Complexity == complexity.Value);

        var active = await query.ToListAsync(cancellationToken);

        // Manual process dependencies live on Order. Load separately, keyed
        // by orderId for fast lookup. Category-level deps used as fallback
        // when an order has no manual processes (same fallback the MasterView
        // query uses — keeps the live "spreman" indicator consistent with
        // this chart).
        var orderIds = active.Select(p => p.OrderItem.OrderId).Distinct().ToList();
        var manualDepsByOrder = await _ordersDb.Orders
            .AsNoTracking()
            .Where(o => orderIds.Contains(o.Id))
            .Select(o => new { o.Id, Deps = o.ManualProcessDependencies.ToList(), HasManual = o.ManualProcesses.Any() })
            .ToDictionaryAsync(x => x.Id, x => x, cancellationToken);

        var categoryDeps = await _productionDb.ProductCategoryDependencies
            .AsNoTracking()
            .Where(d => d.TenantId == tenantId)
            .ToListAsync(cancellationToken);
        var categoryDepsByCategory = categoryDeps
            .GroupBy(d => d.ProductCategoryId)
            .ToDictionary(g => g.Key, g => g.ToList());

        List<Guid> ResolveDeps(OrderItemProcess p)
        {
            var orderInfo = manualDepsByOrder.GetValueOrDefault(p.OrderItem.OrderId);
            if (orderInfo is not null && orderInfo.HasManual)
            {
                return orderInfo.Deps
                    .Where(d => d.ProcessId == p.ProcessId)
                    .Select(d => d.DependsOnProcessId)
                    .ToList();
            }
            return categoryDepsByCategory.TryGetValue(p.OrderItem.ProductCategoryId, out var cd)
                ? cd.Where(d => d.ProcessId == p.ProcessId).Select(d => d.DependsOnProcessId).ToList()
                : new List<Guid>();
        }

        bool IsReady(OrderItemProcess p)
        {
            if (p.Status != ProcessStatus.Pending) return false;
            var deps = ResolveDeps(p);
            if (deps.Count == 0) return true;
            return deps.All(depId =>
            {
                var depProc = p.OrderItem.Processes.FirstOrDefault(ip => ip.ProcessId == depId);
                if (depProc == null) return true; // dep not on this item = effectively withdrawn
                return depProc.Status == ProcessStatus.Completed || depProc.IsWithdrawn;
            });
        }

        var buckets = processes.Select(pr =>
        {
            int inProgress = 0, ready = 0, blocked = 0;
            foreach (var p in active.Where(x => x.ProcessId == pr.Id))
            {
                if (p.Status == ProcessStatus.InProgress) inProgress++;
                else if (p.Status == ProcessStatus.Blocked) blocked++;
                else if (p.Status == ProcessStatus.Pending && IsReady(p)) ready++;
            }
            return new ActiveProcessFunnelBucketDto(
                pr.Id, pr.Code, pr.Name, pr.SequenceOrder,
                inProgress, ready, blocked);
        }).ToList();

        return new ActiveProcessFunnelDto(buckets);
    }
}
