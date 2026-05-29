using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Application.DTOs;
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
        var stats = await ComputeWorkerDayStatsAsync(tenantId, from, to, userId, cancellationToken);

        var workers = stats
            .GroupBy(s => new { s.UserId, s.FullName })
            .Select(g =>
            {
                var daily = g
                    .OrderBy(d => d.Date)
                    .Select(d => new WorkerHoursDayDto(
                        d.Date, d.FirstCheckIn, d.LastCheckOut,
                        d.RegularMinutes, d.OvertimeMinutes, d.TotalWorkedMinutes,
                        d.EffectiveMinutes, d.ActiveMinutes, d.UncoveredMinutes,
                        d.EfficiencyPercent, d.SessionCount))
                    .ToList();

                var totalEffective = g.Sum(x => x.EffectiveMinutes);
                var totalActive = g.Sum(x => x.ActiveMinutes);
                var efficiency = totalEffective > 0
                    ? Math.Round(100.0 * totalActive / totalEffective, 1)
                    : 0.0;

                return new WorkerHoursSummaryDto(
                    g.Key.UserId,
                    g.Key.FullName,
                    g.Sum(x => x.RegularMinutes),
                    g.Sum(x => x.OvertimeMinutes),
                    g.Sum(x => x.TotalWorkedMinutes),
                    totalEffective,
                    totalActive,
                    g.Sum(x => x.UncoveredMinutes),
                    efficiency,
                    daily);
            })
            .OrderBy(w => w.FullName)
            .ToList();

        return new WorkerHoursReportDto(workers);
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

        // Per Bojan review 27.05.2026 — third interpretation attempt for the
        // Trend chart MIN/MAX. Rounds 1 (Excel MINIFS/MAXIFS) and 2 (literal
        // μ±σ on raw data) were both flagged wrong. Switching to two-pass
        // robust stats: cleaned data → μ′ ± σ′ as the band. Same Realni prosek
        // (cleaned mean) but the band is now tight and visually meaningful
        // even with one borderline outlier. See ReportingStats.ComputeRobustTrendStats.
        var buckets = entities
            .Select(p => new { p.CompletedAt, Minutes = EffectiveDurationSeconds(p) / 60.0 })
            .GroupBy(x => BucketStart(x.CompletedAt!.Value, granularity))
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var values = g.Select(x => x.Minutes).ToList();
                var stats = ReportingStats.ComputeRobustTrendStats(values);
                return new ProcessTimeTrendBucketDto(
                    g.Key,
                    stats.Count,
                    stats.TrimmedMeanMinutes,
                    stats.MinMinutes,
                    stats.MaxMinutes);
            })
            .ToList();

        // Normativ = 85% of cleaned trimmed mean across ALL filtered samples
        // (not bucket-aware) so it's a single constant target line.
        var overallValues = entities
            .Select(p => EffectiveDurationSeconds(p) / 60.0)
            .ToList();
        var overallStats = ReportingStats.ComputeRobustTrendStats(overallValues);
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

    public async Task<BlocksPerProcessReportDto> GetBlocksPerProcessAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        // Bojan's spec 25.05.2026: duration of a block is in WORKING HOURS
        // (intersection of CreatedAt → HandledAt with the union of all
        // active Shift windows). Rejected blocks count toward "submitted"
        // but contribute zero duration to the average.

        var processes = await _productionDb.Processes
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .OrderBy(p => p.SequenceOrder)
            .Select(p => new { p.Id, p.Code, p.Name, p.SequenceOrder })
            .ToListAsync(cancellationToken);

        var subProcessToProcess = await _productionDb.SubProcesses
            .AsNoTracking()
            .Where(sp => sp.TenantId == tenantId)
            .Select(sp => new { sp.Id, sp.ProcessId })
            .ToDictionaryAsync(x => x.Id, x => x.ProcessId, cancellationToken);

        var query = _ordersDb.BlockRequests
            .AsNoTracking()
            .Where(b => b.TenantId == tenantId);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            query = query.Where(b => b.CreatedAt >= fromUtc);
        }
        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            query = query.Where(b => b.CreatedAt <= toUtc);
        }

        var blocks = await query
            .Select(b => new
            {
                b.Id,
                b.OrderItemProcessId,
                b.OrderItemSubProcessId,
                b.Status,
                b.CreatedAt,
                b.HandledAt,
            })
            .ToListAsync(cancellationToken);

        // Map each block to its process id. Most blocks reference an OIP
        // directly; sub-process blocks need to be walked back to their
        // parent process via the SubProcess → Process mapping.
        var oipIds = blocks
            .Where(b => b.OrderItemProcessId.HasValue)
            .Select(b => b.OrderItemProcessId!.Value)
            .Distinct()
            .ToList();
        var oipToProcess = await _ordersDb.OrderItemProcesses
            .AsNoTracking()
            .Where(p => oipIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ProcessId })
            .ToDictionaryAsync(x => x.Id, x => x.ProcessId, cancellationToken);

        var oispIds = blocks
            .Where(b => b.OrderItemSubProcessId.HasValue)
            .Select(b => b.OrderItemSubProcessId!.Value)
            .Distinct()
            .ToList();
        var oispToSubProcessId = await _ordersDb.OrderItemSubProcesses
            .AsNoTracking()
            .Where(sp => oispIds.Contains(sp.Id))
            .Select(sp => new { sp.Id, sp.SubProcessId })
            .ToDictionaryAsync(x => x.Id, x => x.SubProcessId, cancellationToken);

        Guid? ResolveProcessId(Guid? oipId, Guid? oispId)
        {
            if (oipId.HasValue && oipToProcess.TryGetValue(oipId.Value, out var pid))
                return pid;
            if (oispId.HasValue
                && oispToSubProcessId.TryGetValue(oispId.Value, out var subId)
                && subProcessToProcess.TryGetValue(subId, out var pid2))
                return pid2;
            return null;
        }

        // Working-hours math — see WorkingMinutesBetween below.
        var shifts = await _identityDb.Shifts
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .Select(s => new { s.StartTime, s.EndTime })
            .ToListAsync(cancellationToken);
        var shiftWindows = shifts
            .Select(s => (Start: s.StartTime, End: s.EndTime))
            .ToList();

        double DurationHours(DateTime createdAt, DateTime? handledAt) =>
            handledAt.HasValue
                ? WorkingMinutesBetween(createdAt, handledAt.Value, shiftWindows) / 60.0
                : 0.0;

        var result = new List<BlocksPerProcessBucketDto>();
        foreach (var pr in processes)
        {
            var procBlocks = blocks
                .Where(b => ResolveProcessId(b.OrderItemProcessId, b.OrderItemSubProcessId) == pr.Id)
                .ToList();

            var totalSubmitted = procBlocks.Count;
            var approved = procBlocks.Count(b =>
                b.Status == RequestStatus.Approved || b.Status == RequestStatus.Resolved);
            var resolved = procBlocks.Count(b => b.Status == RequestStatus.Resolved);
            var rejected = procBlocks.Count(b => b.Status == RequestStatus.Rejected);

            // Average over approved+resolved blocks, EXCLUDING those with 0
            // working hours (Bojan 29.05.2026: "izbaciti 0" — blocks opened and
            // resolved entirely outside shift windows shouldn't dilute the
            // average). Counts above are unaffected; only the average drops them.
            double avgDurationHours = 0;
            var nonZeroDurations = procBlocks
                .Where(b => b.Status == RequestStatus.Approved || b.Status == RequestStatus.Resolved)
                .Select(b => DurationHours(b.CreatedAt, b.HandledAt))
                .Where(h => h > 0)
                .ToList();
            if (nonZeroDurations.Count > 0)
                avgDurationHours = nonZeroDurations.Sum() / nonZeroDurations.Count;

            result.Add(new BlocksPerProcessBucketDto(
                pr.Id, pr.Code, pr.Name, pr.SequenceOrder,
                totalSubmitted, approved, resolved, rejected,
                Math.Round(avgDurationHours, 2)));
        }

        return new BlocksPerProcessReportDto(result);
    }

    public async Task<ProductManufacturingTimeReportDto> GetProductManufacturingTimeAsync(
        Guid tenantId,
        DateTime? from,
        DateTime? to,
        List<OrderType>? orderTypes,
        List<Guid>? productCategoryIds,
        CancellationToken cancellationToken = default)
    {
        // Per Bojan spec 25.05.2026 — one row per COMPLETED order, all
        // processes ordered by StartedAt. Overlap clipping: if N+1 starts
        // before N completes, gap = 0 (no negative gaps allowed).
        var orderQuery = _ordersDb.Orders
            .AsNoTracking()
            .Where(o => o.TenantId == tenantId && o.Status == OrderStatus.Completed);

        if (from.HasValue)
        {
            var fromUtc = DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc);
            orderQuery = orderQuery.Where(o => o.CompletedAt >= fromUtc);
        }
        if (to.HasValue)
        {
            var toUtc = DateTime.SpecifyKind(to.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc);
            orderQuery = orderQuery.Where(o => o.CompletedAt <= toUtc);
        }
        if (orderTypes is { Count: > 0 })
            orderQuery = orderQuery.Where(o => orderTypes.Contains(o.OrderType));

        var orders = await orderQuery
            .Select(o => new { o.Id, o.OrderNumber, o.OrderType, o.CompletedAt })
            .ToListAsync(cancellationToken);

        if (orders.Count == 0)
            return new ProductManufacturingTimeReportDto(new List<ProductManufacturingTimeOrderDto>());

        var orderIds = orders.Select(o => o.Id).ToList();

        var items = await _ordersDb.OrderItems
            .AsNoTracking()
            .Where(i => orderIds.Contains(i.OrderId))
            .Select(i => new { i.Id, i.OrderId, i.ProductCategoryId, i.Quantity })
            .ToListAsync(cancellationToken);

        // Optional product-category filter — since rows are per ITEM, keep
        // only the items whose category matches (and drop orders left with no
        // matching item).
        if (productCategoryIds is { Count: > 0 })
        {
            items = items.Where(i => productCategoryIds.Contains(i.ProductCategoryId)).ToList();
            var allowedOrderIds = items.Select(i => i.OrderId).Distinct().ToHashSet();
            orders = orders.Where(o => allowedOrderIds.Contains(o.Id)).ToList();
            orderIds = orders.Select(o => o.Id).ToList();
        }

        if (orders.Count == 0)
            return new ProductManufacturingTimeReportDto(new List<ProductManufacturingTimeOrderDto>());

        var itemIds = items.Select(i => i.Id).ToList();
        var oips = await _ordersDb.OrderItemProcesses
            .AsNoTracking()
            .Where(p => itemIds.Contains(p.OrderItemId) && p.Status == ProcessStatus.Completed && p.StartedAt.HasValue && p.CompletedAt.HasValue)
            .Select(p => new
            {
                p.Id,
                p.OrderItemId,
                p.ProcessId,
                p.Complexity,
                p.StartedAt,
                p.CompletedAt,
                // Active/effective time (Bojan 29.05.2026, List 2 Q2): only the
                // operator's logged work counts, NOT the wall-clock Start→Complete
                // span. Sub-process sum when present, else the OIP's own active-
                // timer total. Both live in the misnamed "Minutes" field that in
                // fact stores SECONDS (see EffectiveDurationSeconds).
                ParentSeconds = p.TotalDurationMinutes,
                SubSeconds = p.SubProcesses
                    .Where(sp => !sp.IsWithdrawn && sp.TotalDurationMinutes > 0)
                    .Sum(sp => (int?)sp.TotalDurationMinutes) ?? 0,
            })
            .ToListAsync(cancellationToken);

        var processIds = oips.Select(p => p.ProcessId).Distinct().ToList();
        var processes = await _productionDb.Processes
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId && processIds.Contains(p.Id))
            .Select(p => new { p.Id, p.Code, p.Name, p.SequenceOrder })
            .ToDictionaryAsync(x => x.Id, cancellationToken);

        var categoryIds = items.Select(i => i.ProductCategoryId).Distinct().ToList();
        var categories = await _productionDb.ProductCategories
            .AsNoTracking()
            .Where(c => c.TenantId == tenantId && categoryIds.Contains(c.Id))
            .Select(c => new { c.Id, c.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, cancellationToken);

        var itemsByOrder = items.GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g => g.ToList());
        var oipsByItem = oips.GroupBy(p => p.OrderItemId).ToDictionary(g => g.Key, g => g.ToList());

        var result = new List<ProductManufacturingTimeOrderDto>();
        // One row per ORDER ITEM (Sale/Bojan 29.05.2026: "sve stavke iz
        // narudžbine treba da budu prikazane"). Processes are ordered by the
        // canonical sequence so columns line up with the order-detail table,
        // and gaps are computed between consecutive processes of THAT item.
        // The old per-order aggregation collapsed every item's processes into
        // shared MIN/MAX slots, which overlapped and produced impossible
        // 0:00:00 gaps for multi-item orders.
        foreach (var order in orders.OrderByDescending(o => o.CompletedAt))
        {
            if (!itemsByOrder.TryGetValue(order.Id, out var orderItems) || orderItems.Count == 0)
                continue;

            foreach (var item in orderItems.OrderBy(i => i.Id))
            {
                if (!oipsByItem.TryGetValue(item.Id, out var itemOips) || itemOips.Count == 0)
                    continue;

                // Najzastupljenija težina — mode of THIS item's process
                // complexities, low-bias tie-break (T/S→S, S/L→L, T/L→L, all→L).
                var topComplexity = ResolveTopComplexity(itemOips.Select(p => p.Complexity));
                var complexityShare = ResolveComplexityShare(itemOips.Select(p => p.Complexity));

                // Order by process SequenceOrder (matches the order table);
                // StartedAt breaks ties. Collapse the rare case of multiple
                // OIPs for one process via MIN(start)/MAX(complete).
                var processSlots = itemOips
                    .Where(p => processes.ContainsKey(p.ProcessId))
                    .GroupBy(p => p.ProcessId)
                    .Select(g => new
                    {
                        ProcessId = g.Key,
                        Sequence = processes[g.Key].SequenceOrder,
                        StartedAt = g.Min(x => x.StartedAt!.Value),
                        CompletedAt = g.Max(x => x.CompletedAt!.Value),
                        // Σ active time over the OIP(s) of this process.
                        ActiveSeconds = g.Sum(x => x.SubSeconds > 0 ? x.SubSeconds : x.ParentSeconds),
                    })
                    .OrderBy(g => g.Sequence)
                    .ThenBy(g => g.StartedAt)
                    .ToList();

                if (processSlots.Count == 0)
                    continue;

                var processDtos = new List<ProductManufacturingProcessDto>(processSlots.Count);
                var totalWithoutGaps = 0;
                var totalWithGaps = 0;

                for (int idx = 0; idx < processSlots.Count; idx++)
                {
                    var slot = processSlots[idx];
                    var procInfo = processes[slot.ProcessId];

                    // Active time, not Stop−Start (Bojan 29.05.2026, List 2 Q2).
                    var durationSec = Math.Max(0, slot.ActiveSeconds);
                    totalWithoutGaps += durationSec;

                    var gapSec = 0;
                    if (idx + 1 < processSlots.Count)
                    {
                        var next = processSlots[idx + 1];
                        // "Do sledećeg procesa" = max(0, next.start − this.stop)
                        // (Excel L formula — overlaps/out-of-order give 0).
                        var rawGap = (int)(next.StartedAt - slot.CompletedAt).TotalSeconds;
                        gapSec = Math.Max(0, rawGap);
                    }

                    processDtos.Add(new ProductManufacturingProcessDto(
                        procInfo.Id,
                        procInfo.Code,
                        procInfo.Name,
                        procInfo.SequenceOrder,
                        slot.StartedAt,
                        slot.CompletedAt,
                        durationSec,
                        gapSec));

                    totalWithGaps += durationSec + gapSec;
                }

                var categoryName = categories.TryGetValue(item.ProductCategoryId, out var n) ? n : "—";

                result.Add(new ProductManufacturingTimeOrderDto(
                    order.Id,
                    item.Id,
                    order.OrderNumber,
                    order.OrderType.ToString(),
                    categoryName,
                    topComplexity,
                    complexityShare,
                    processDtos,
                    totalWithGaps,
                    totalWithoutGaps));
            }
        }

        return new ProductManufacturingTimeReportDto(result);
    }

    /// <summary>
    /// "Najzastupljenija težina" — mode of complexities across all of an
    /// order's OIPs, with Bojan's low-bias tie-break rules (25.05.2026):
    ///   T/S equal → S, S/L equal → L, T/L equal → L, all three tied → L.
    /// Null entries are skipped; returns null if no complexity is set.
    /// </summary>
    private static string? ResolveTopComplexity(IEnumerable<ComplexityType?> complexities)
    {
        var counts = complexities
            .Where(c => c.HasValue)
            .GroupBy(c => c!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        if (counts.Count == 0) return null;

        var t = counts.TryGetValue(ComplexityType.T, out var tc) ? tc : 0;
        var s = counts.TryGetValue(ComplexityType.S, out var sc) ? sc : 0;
        var l = counts.TryGetValue(ComplexityType.L, out var lc) ? lc : 0;

        // Strict majority cases first.
        if (l > s && l > t) return "L";
        if (s > l && s > t) return "S";
        if (t > s && t > l) return "T";

        // Two-way and three-way ties — apply low-bias rules.
        if (t == s && s == l) return "L";       // all tied → L
        if (s == l && s >= t) return "L";        // S/L tie → L
        if (t == l && l >= s) return "L";        // T/L tie → L
        if (t == s && s >= l) return "S";        // T/S tie → S

        // Fallback (shouldn't reach here, but kept defensive).
        if (l >= s && l >= t) return "L";
        if (s >= t) return "S";
        return "T";
    }

    /// <summary>
    /// "Zastupljenost težina" — T/S/L distribution across an item's OIP
    /// complexities, formatted "{t}% / {s}% / {l}%" (Excel G column,
    /// ROUND(count/total*100,0)). Null when no complexity is set.
    /// </summary>
    private static string? ResolveComplexityShare(IEnumerable<ComplexityType?> complexities)
    {
        var present = complexities.Where(c => c.HasValue).Select(c => c!.Value).ToList();
        if (present.Count == 0) return null;
        double total = present.Count;
        var t = (int)Math.Round(100.0 * present.Count(c => c == ComplexityType.T) / total);
        var s = (int)Math.Round(100.0 * present.Count(c => c == ComplexityType.S) / total);
        var l = (int)Math.Round(100.0 * present.Count(c => c == ComplexityType.L) / total);
        return $"{t}% / {s}% / {l}%";
    }

    public async Task<WorkEfficiencyReportDto> GetWorkEfficiencyAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        Guid? userId,
        CancellationToken cancellationToken = default)
    {
        // Excel Table 2 (Sale/Bojan 29.05.2026): one row per WORKER over the
        // filtered period. Per-day detail lives in the "Sati radnika" tab; this
        // is just the per-worker aggregate (+ charts on the FE). Both come from
        // the same per-worker-per-day computation so the two tabs never drift.
        var stats = await ComputeWorkerDayStatsAsync(tenantId, from, to, userId, cancellationToken);

        var rows = stats
            .GroupBy(s => new { s.UserId, s.FullName })
            .Select(g =>
            {
                var totalEffective = g.Sum(x => x.EffectiveMinutes);
                var totalActive = g.Sum(x => x.ActiveMinutes);
                var efficiency = totalEffective > 0
                    ? Math.Round(100.0 * totalActive / totalEffective, 1)
                    : 0.0;
                return new WorkEfficiencyRowDto(
                    g.Key.UserId,
                    g.Key.FullName,
                    g.Sum(x => x.TotalWorkedMinutes),
                    totalEffective,
                    totalActive,
                    g.Sum(x => x.UncoveredMinutes),
                    efficiency);
            })
            .OrderBy(r => r.FullName)
            .ToList();

        return new WorkEfficiencyReportDto(rows);
    }

    public async Task<ActiveWorkSessionDto?> GetActiveWorkSessionAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Tablet alarm endpoint (Bojan spec 25.05.2026, lazy approach
        // 26.05.2026). Returns the worker's open session + pre-computed
        // alarmAtUtc/logoutAtUtc so the tablet can drive a client-side
        // countdown without polling the server.
        var open = await _ordersDb.WorkSessions
            .AsNoTracking()
            .Where(ws => ws.TenantId == tenantId
                && ws.UserId == userId
                && ws.CheckOutTime == null)
            .OrderByDescending(ws => ws.CheckInTime)
            .Select(ws => new
            {
                ws.Id,
                ws.UserId,
                ws.CheckInTime,
                ws.CheckOutTime,
                ws.DurationMinutes,
                ws.Date,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (open == null) return null;

        var sessionDto = new WorkSessionDto(
            open.Id,
            open.UserId,
            open.CheckInTime,
            open.CheckOutTime,
            open.DurationMinutes,
            open.Date,
            IsActive: true);

        var shifts = await _identityDb.Shifts
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .Select(s => new
            {
                s.StartTime,
                s.EndTime,
                s.MaxOvertimeHours,
                s.AlarmBeforeLogoutMinutes,
            })
            .ToListAsync(cancellationToken);

        var checkInTimeOfDay = TimeOnly.FromDateTime(open.CheckInTime);
        var shift = shifts.FirstOrDefault(s => IsTimeInShift(checkInTimeOfDay, s.StartTime, s.EndTime));

        if (shift == null)
            return new ActiveWorkSessionDto(sessionDto, null, null);

        var shiftDuration = ShiftDuration(shift.StartTime, shift.EndTime);
        var logoutAt = open.CheckInTime.Add(shiftDuration).AddHours(shift.MaxOvertimeHours);
        var alarmAt = logoutAt.AddMinutes(-shift.AlarmBeforeLogoutMinutes);

        return new ActiveWorkSessionDto(sessionDto, alarmAt, logoutAt);
    }

    /// <summary>
    /// Shared per-worker-per-day computation behind both the "Sati radnika"
    /// (daily detail) and "Efikasnost radnog vremena" (per-worker aggregate)
    /// tabs — Excel "Efikasnost radnog vremena" Table 1, Sale/Bojan 29.05.2026.
    ///
    /// Combines all of a worker's sessions for a day (lazy auto-logout cap
    /// applied per session), splits regular/overtime at the matched shift's
    /// duration, subtracts the shift break once, and unions sub-process logs
    /// for "Aktivno na procesima". Assumptions (one row/day, break once/day,
    /// regular cap = shift duration) are pending Sale/Bojan confirmation.
    /// </summary>
    private async Task<List<WorkerDayStat>> ComputeWorkerDayStatsAsync(
        Guid tenantId,
        DateOnly from,
        DateOnly to,
        Guid? userId,
        CancellationToken cancellationToken)
    {
        // These tabs are about factory-floor workers only — restrict to the
        // Department role. Admin / Manager / Coordinator / SalesManager /
        // SuperAdmin may have a (test) check-in session but aren't "radnici".
        // (Confirmed by Milos 29.05.2026: only the worker role belongs here.)
        var workerIds = await _identityDb.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.Role == UserRole.Department)
            .Select(u => u.Id)
            .ToListAsync(cancellationToken);

        var sessionsQuery = _ordersDb.WorkSessions
            .AsNoTracking()
            .Where(ws => ws.TenantId == tenantId && ws.Date >= from && ws.Date <= to
                && workerIds.Contains(ws.UserId));
        if (userId.HasValue)
            sessionsQuery = sessionsQuery.Where(ws => ws.UserId == userId.Value);

        var rawSessions = await sessionsQuery
            .Select(ws => new { ws.UserId, ws.Date, ws.CheckInTime, ws.CheckOutTime })
            .ToListAsync(cancellationToken);

        var shiftConfigs = await _identityDb.Shifts
            .AsNoTracking()
            .Where(s => s.TenantId == tenantId && s.IsActive)
            .Select(s => new ShiftConfig(s.StartTime, s.EndTime, s.MaxOvertimeHours, s.BreakMinutes))
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        // Apply the lazy auto-logout cap per session; drop still-running
        // sessions that haven't hit their cap yet (effectiveEnd == null).
        var sessions = rawSessions
            .Select(s =>
            {
                var effectiveEnd = ComputeEffectiveSessionEnd(s.CheckInTime, s.CheckOutTime, now, shiftConfigs);
                if (effectiveEnd == null) return null;
                var duration = (int)(effectiveEnd.Value - s.CheckInTime).TotalMinutes;
                return new
                {
                    s.UserId,
                    s.Date,
                    s.CheckInTime,
                    CheckOut = effectiveEnd.Value,
                    Duration = Math.Max(0, duration),
                };
            })
            .Where(x => x != null)
            .Select(x => x!)
            .ToList();

        if (sessions.Count == 0)
            return new List<WorkerDayStat>();

        // Sub-process logs in the same window → wall-clock union per (user, day).
        var fromUtc = DateTime.SpecifyKind(from.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        var logsQuery = _ordersDb.OrderItemSubProcessLogs
            .AsNoTracking()
            .Where(l => l.TenantId == tenantId
                && l.EndTime.HasValue
                && l.StartTime >= fromUtc
                && l.StartTime < toUtc
                && workerIds.Contains(l.UserId));
        if (userId.HasValue)
            logsQuery = logsQuery.Where(l => l.UserId == userId.Value);

        var logs = await logsQuery
            .Select(l => new { l.UserId, l.StartTime, EndTime = l.EndTime!.Value })
            .ToListAsync(cancellationToken);

        var logsByUserDay = logs
            .GroupBy(l => new { l.UserId, Date = DateOnly.FromDateTime(l.StartTime) })
            .ToDictionary(
                g => g.Key,
                g => UnionMinutes(g.Select(x => (x.StartTime, x.EndTime)).ToList()));

        var userIds = sessions.Select(s => s.UserId).Distinct().ToList();
        var users = await _identityDb.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}", cancellationToken);

        ShiftConfig? MatchShift(DateTime checkIn)
        {
            if (shiftConfigs.Count == 0) return null;
            var tod = TimeOnly.FromDateTime(checkIn);
            return shiftConfigs.FirstOrDefault(s => IsTimeInShift(tod, s.StartTime, s.EndTime));
        }

        return sessions
            .GroupBy(s => new { s.UserId, s.Date })
            .Select(g =>
            {
                var totalWorked = g.Sum(s => s.Duration);
                var firstCheckIn = g.Min(s => s.CheckInTime);
                var lastCheckOut = g.Max(s => s.CheckOut);

                var shift = MatchShift(firstCheckIn);
                var regularCap = shift != null
                    ? (int)ShiftDuration(shift.StartTime, shift.EndTime).TotalMinutes
                    : 8 * 60;
                var breakMinutes = shift?.BreakMinutes ?? 0;

                var regular = Math.Min(totalWorked, regularCap);
                var overtime = Math.Max(0, totalWorked - regularCap);
                var effective = Math.Max(0, totalWorked - breakMinutes);

                var active = logsByUserDay.TryGetValue(new { g.Key.UserId, g.Key.Date }, out var a) ? a : 0;
                active = Math.Min(active, effective);
                var uncovered = Math.Max(0, effective - active);
                var efficiency = effective > 0 ? Math.Round(100.0 * active / effective, 1) : 0.0;

                return new WorkerDayStat(
                    g.Key.UserId,
                    users.GetValueOrDefault(g.Key.UserId, "Unknown"),
                    g.Key.Date,
                    firstCheckIn,
                    lastCheckOut,
                    regular,
                    overtime,
                    totalWorked,
                    effective,
                    active,
                    uncovered,
                    efficiency,
                    g.Count());
            })
            .ToList();
    }

    private record ShiftConfig(TimeOnly StartTime, TimeOnly EndTime, int MaxOvertimeHours, int BreakMinutes);

    private record WorkerDayStat(
        Guid UserId,
        string FullName,
        DateOnly Date,
        DateTime? FirstCheckIn,
        DateTime? LastCheckOut,
        int RegularMinutes,
        int OvertimeMinutes,
        int TotalWorkedMinutes,
        int EffectiveMinutes,
        int ActiveMinutes,
        int UncoveredMinutes,
        double EfficiencyPercent,
        int SessionCount);

    /// <summary>
    /// Lazy auto-logout per Bojan 25.05.2026 — applies to forgotten checkouts
    /// at report-time (no background job needed; see chat 26.05.2026).
    ///
    /// Returns the effective CheckOutTime for a session:
    ///   • Find the shift whose time-of-day window contains CheckInTime; if
    ///     none matches OR no shifts are configured, return CheckOutTime
    ///     as-is (can't cap without per-shift config).
    ///   • cap = CheckIn + ShiftDuration + MaxOvertime.
    ///   • If CheckOutTime is set: return min(CheckOutTime, cap). Closed
    ///     sessions with absurd durations (worker checked out a day late)
    ///     also get clamped — Bojan's "restrict extreme cases" intent.
    ///   • If still open: if now ≥ cap → return cap; else → null (session
    ///     is still legitimately running, exclude from reports).
    /// </summary>
    private static DateTime? ComputeEffectiveSessionEnd(
        DateTime checkIn,
        DateTime? checkOut,
        DateTime now,
        List<ShiftConfig> shifts)
    {
        if (shifts.Count == 0) return checkOut;

        var checkInTime = TimeOnly.FromDateTime(checkIn);
        var shift = shifts.FirstOrDefault(s => IsTimeInShift(checkInTime, s.StartTime, s.EndTime));
        if (shift == null) return checkOut;

        var shiftDuration = ShiftDuration(shift.StartTime, shift.EndTime);
        var cap = checkIn.Add(shiftDuration).AddHours(shift.MaxOvertimeHours);

        if (checkOut.HasValue)
            return checkOut.Value > cap ? cap : checkOut.Value;

        return now >= cap ? cap : null;
    }

    private static bool IsTimeInShift(TimeOnly t, TimeOnly start, TimeOnly end)
    {
        // Normal shift (e.g. 06:00–14:00): [start, end).
        // Cross-midnight shift (e.g. 22:00–06:00): t ≥ start OR t < end.
        if (end > start) return t >= start && t < end;
        return t >= start || t < end;
    }

    private static TimeSpan ShiftDuration(TimeOnly start, TimeOnly end)
    {
        var startSpan = start.ToTimeSpan();
        var endSpan = end > start
            ? end.ToTimeSpan()
            : end.ToTimeSpan().Add(TimeSpan.FromHours(24));
        return endSpan - startSpan;
    }

    /// <summary>
    /// Wall-clock union of a set of [start, end] intervals — total minutes
    /// covered, overlapping ranges counted once. Used by the Efikasnost
    /// report's "Aktivno na procesima" column so parallel sub-processes
    /// (worker running two timers at once) don't double-count.
    /// </summary>
    private static int UnionMinutes(List<(DateTime Start, DateTime End)> intervals)
    {
        if (intervals.Count == 0) return 0;
        var sorted = intervals
            .Where(i => i.End > i.Start)
            .OrderBy(i => i.Start)
            .ToList();
        if (sorted.Count == 0) return 0;

        var totalSeconds = 0L;
        var curStart = sorted[0].Start;
        var curEnd = sorted[0].End;
        for (int i = 1; i < sorted.Count; i++)
        {
            var (s, e) = sorted[i];
            if (s <= curEnd)
            {
                // Overlap — extend the current merged interval.
                if (e > curEnd) curEnd = e;
            }
            else
            {
                totalSeconds += (long)(curEnd - curStart).TotalSeconds;
                curStart = s;
                curEnd = e;
            }
        }
        totalSeconds += (long)(curEnd - curStart).TotalSeconds;
        return (int)(totalSeconds / 60);
    }

    /// <summary>
    /// Working minutes between two timestamps — sum of [from, to] ∩ union
    /// of daily Shift windows. Used by /reports/blocks-per-process so a
    /// block sitting overnight or over a weekend doesn't accumulate
    /// non-working hours (Bojan spec 25.05.2026: "samo radni period —
    /// vreme svih aktivnih smena").
    ///
    /// Handles shifts that cross midnight (e.g., 22:00–06:00) by splitting
    /// the day's intersection into [start..24:00] + [00:00..end].
    /// </summary>
    private static int WorkingMinutesBetween(
        DateTime from,
        DateTime to,
        List<(TimeOnly Start, TimeOnly End)> shifts)
    {
        if (to <= from) return 0;
        if (shifts.Count == 0) return 0;

        // Walk day-by-day from `from`'s date through `to`'s date and collect
        // each shift's window (clipped to [from, to]). The windows are then
        // UNIONed so overlapping active shifts count once — the previous
        // per-shift sum double-counted any overlap (the doc always said
        // "union", the code summed).
        var fromUtc = DateTime.SpecifyKind(from, DateTimeKind.Utc);
        var toUtc = DateTime.SpecifyKind(to, DateTimeKind.Utc);

        var windows = new List<(DateTime Start, DateTime End)>();
        var dayCursor = fromUtc.Date;
        var lastDay = toUtc.Date;

        void AddClipped(DateTime winStart, DateTime winEnd)
        {
            var s = winStart > fromUtc ? winStart : fromUtc;
            var e = winEnd < toUtc ? winEnd : toUtc;
            if (e > s) windows.Add((s, e));
        }

        while (dayCursor <= lastDay)
        {
            foreach (var (start, end) in shifts)
            {
                // Build the shift's actual time window for this day. If End
                // ≤ Start, the shift crosses midnight: split into two.
                if (end > start)
                {
                    AddClipped(
                        DateTime.SpecifyKind(dayCursor.Add(start.ToTimeSpan()), DateTimeKind.Utc),
                        DateTime.SpecifyKind(dayCursor.Add(end.ToTimeSpan()), DateTimeKind.Utc));
                }
                else
                {
                    // Cross-midnight: [Start, 24:00) on dayCursor + [00:00, End) on next day.
                    AddClipped(
                        DateTime.SpecifyKind(dayCursor.Add(start.ToTimeSpan()), DateTimeKind.Utc),
                        DateTime.SpecifyKind(dayCursor.AddDays(1), DateTimeKind.Utc));
                    AddClipped(
                        DateTime.SpecifyKind(dayCursor.AddDays(1), DateTimeKind.Utc),
                        DateTime.SpecifyKind(dayCursor.AddDays(1).Add(end.ToTimeSpan()), DateTimeKind.Utc));
                }
            }
            dayCursor = dayCursor.AddDays(1);
        }

        return UnionMinutes(windows);
    }
}
