using AlGreenMES.Modules.Identity.Infrastructure.Persistence;
using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Interfaces;
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

        var query = _ordersDb.OrderItemProcesses
            .AsNoTracking()
            .Include(p => p.OrderItem)
                .ThenInclude(oi => oi.Order)
            .Where(p => p.TenantId == tenantId
                && p.Status == ProcessStatus.Completed
                && p.Complexity.HasValue
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

        if (productCategoryIds is { Count: > 0 })
            query = query.Where(p => productCategoryIds.Contains(p.OrderItem.ProductCategoryId));

        if (orderTypes is { Count: > 0 })
            query = query.Where(p => orderTypes.Contains(p.OrderItem.Order.OrderType));

        // TotalDurationMinutes column actually stores seconds (misnamed) — divide by 60.
        var rows = await query
            .Select(p => new { p.ProcessId, p.Complexity, Seconds = p.TotalDurationMinutes })
            .ToListAsync(cancellationToken);

        var grouped = rows
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

    private static ComplexityStatsDto ComputeStats(List<double> values)
    {
        var n = values.Count;
        var mean = values.Average();
        var variance = values.Sum(x => (x - mean) * (x - mean)) / n;
        var stdev = Math.Sqrt(variance);
        var min = values.Min();
        var max = values.Max();

        double trimmedMean;
        if (n == 1 || stdev == 0)
        {
            trimmedMean = mean;
        }
        else
        {
            var lower = mean - stdev;
            var upper = mean + stdev;
            var trimmed = values.Where(x => x >= lower && x <= upper).ToList();
            trimmedMean = trimmed.Count > 0 ? trimmed.Average() : mean;
        }

        return new ComplexityStatsDto(
            n,
            Math.Round(mean, 2),
            Math.Round(min, 2),
            Math.Round(max, 2),
            Math.Round(stdev, 2),
            Math.Round(trimmedMean, 2));
    }

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
                p.TotalDurationMinutes,
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
}
