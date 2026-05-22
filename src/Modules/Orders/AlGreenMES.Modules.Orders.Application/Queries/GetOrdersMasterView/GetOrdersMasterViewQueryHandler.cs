using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Production.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.GetOrdersMasterView;

public class GetOrdersMasterViewQueryHandler : IRequestHandler<GetOrdersMasterViewQuery, PagedResult<OrderMasterViewDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductCategoryRepository _categoryRepository;

    public GetOrdersMasterViewQueryHandler(IOrderRepository orderRepository, IProductCategoryRepository categoryRepository)
    {
        _orderRepository = orderRepository;
        _categoryRepository = categoryRepository;
    }

    public async Task<PagedResult<OrderMasterViewDto>> Handle(GetOrdersMasterViewQuery request, CancellationToken cancellationToken)
    {
        var result = await _orderRepository.GetPagedWithProcessesAsync(
            request.TenantId, request.Status, request.OrderType, request.IsInvoiced,
            request.DateFrom, request.DateTo, request.Search,
            request.SortBy, request.IsDescending,
            request.GetPage(), request.GetPageSize(), cancellationToken);

        // Pre-load category dependencies for all categories in the result set
        // via a single batched query (was N+1 round-trips per distinct category).
        var categoryIds = result.Items
            .SelectMany(o => o.Items.Select(i => i.ProductCategoryId))
            .Distinct()
            .ToList();
        var categories = await _categoryRepository.GetByIdsWithDetailsAsync(categoryIds, cancellationToken);
        var categoryDeps = categories
            .Where(c => c.Dependencies != null)
            .ToDictionary(
                c => c.Id,
                c => c.Dependencies
                    .Select(d => (d.ProcessId, d.DependsOnProcessId))
                    .ToList());

        return result.MapItems(o => MapToMasterView(o, categoryDeps));
    }

    private static OrderMasterViewDto MapToMasterView(Order order, Dictionary<Guid, List<(Guid ProcessId, Guid DependsOnProcessId)>> categoryDeps)
    {
        var allProcesses = order.Items.SelectMany(i => i.Processes).ToList();
        var nonWithdrawn = allProcesses.Where(p => !p.IsWithdrawn).ToList();

        var grouped = allProcesses.GroupBy(p => p.ProcessId).ToList();

        var processStatuses = grouped.ToDictionary(
            g => g.Key.ToString(),
            g => AggregateStatus(g).ToString());

        var processDurations = grouped.ToDictionary(
            g => g.Key.ToString(),
            g => g.Sum(p =>
            {
                var hasSubProcesses = p.SubProcesses.Any(sp => !sp.IsWithdrawn);
                if (hasSubProcesses)
                {
                    // Sub-process path: sum sub-process durations + any open log elapsed
                    return p.SubProcesses.Sum(sp =>
                    {
                        var spTime = sp.TotalDurationMinutes;
                        var openLog = sp.Logs?.FirstOrDefault(l => l.EndTime == null);
                        if (openLog != null)
                            spTime += (int)(DateTime.UtcNow - openLog.StartTime).TotalSeconds;
                        return spTime;
                    });
                }
                // No sub-process path: saved + live elapsed
                var saved = p.TotalDurationMinutes;
                if (p.Status == ProcessStatus.InProgress && !p.PausedAt.HasValue)
                {
                    var since = p.ResumedAt ?? p.StartedAt;
                    if (since.HasValue)
                        saved += (int)(DateTime.UtcNow - since.Value).TotalSeconds;
                }
                return saved;
            }));

        // Build process dependencies. When the order has manual processes, those
        // override the category dependencies entirely (manual mode is opt-in via
        // OrderType.AllowsManualProcesses, and the manual list is per-order).
        var processDependencies = new Dictionary<string, List<string>>();
        if (order.HasManualProcesses)
        {
            foreach (var dep in order.ManualProcessDependencies)
            {
                var key = dep.ProcessId.ToString();
                if (!processDependencies.ContainsKey(key))
                    processDependencies[key] = new List<string>();
                var depKey = dep.DependsOnProcessId.ToString();
                if (!processDependencies[key].Contains(depKey))
                    processDependencies[key].Add(depKey);
            }
        }
        else
        {
            foreach (var item in order.Items)
            {
                if (categoryDeps.TryGetValue(item.ProductCategoryId, out var deps))
                {
                    foreach (var dep in deps)
                    {
                        var key = dep.ProcessId.ToString();
                        if (!processDependencies.ContainsKey(key))
                            processDependencies[key] = new List<string>();
                        var depKey = dep.DependsOnProcessId.ToString();
                        if (!processDependencies[key].Contains(depKey))
                            processDependencies[key].Add(depKey);
                    }
                }
            }
        }

        // Build a per-item dep lookup. For manual orders the dep graph is at the
        // order level; for category orders each item's category has its own.
        // (Was: always used categoryDeps — wrong for manual orders, which made
        // manual processes look ready/not-ready based on the wrong rules.)
        var manualDepsByProcess = order.HasManualProcesses
            ? order.ManualProcessDependencies
                .GroupBy(d => d.ProcessId)
                .ToDictionary(g => g.Key, g => g.Select(d => d.DependsOnProcessId).ToList())
            : null;

        List<Guid> GetItemProcDeps(OrderItem item, Guid processId)
        {
            if (manualDepsByProcess is not null)
                return manualDepsByProcess.GetValueOrDefault(processId) ?? new List<Guid>();
            return categoryDeps.TryGetValue(item.ProductCategoryId, out var icd)
                ? icd.Where(d => d.ProcessId == processId).Select(d => d.DependsOnProcessId).ToList()
                : new List<Guid>();
        }

        bool IsItemProcessReady(OrderItem item, OrderItemProcess p)
        {
            if (p.Status != ProcessStatus.Pending) return false;
            var deps = GetItemProcDeps(item, p.ProcessId);
            if (deps.Count == 0) return true; // independent — ready
            return deps.All(depId =>
            {
                var depProc = item.Processes.FirstOrDefault(ip => ip.ProcessId == depId);
                if (depProc == null) return true; // dep not on this item → effectively withdrawn
                return depProc.Status == ProcessStatus.Completed || depProc.IsWithdrawn;
            });
        }

        // Per-item readiness map: itemProcessReady[itemId][processId] = bool.
        // The FE per-item ItemProcessBar consumes this directly — it can't compute
        // it locally because the flat processDependencies map merges deps across
        // categories and gives the wrong answer for multi-item orders.
        var itemProcessReady = new Dictionary<string, Dictionary<string, bool>>();
        foreach (var item in order.Items)
        {
            var map = new Dictionary<string, bool>();
            foreach (var p in item.Processes.Where(p => !p.IsWithdrawn))
                map[p.ProcessId.ToString()] = IsItemProcessReady(item, p);
            itemProcessReady[item.Id.ToString()] = map;
        }

        // Aggregate processReady: a process is ready if at least one item has it
        // ready. Blocked/InProgress on any item suppresses the aggregate ready
        // indicator (matches FE drawer-circles priority).
        var processReady = new Dictionary<string, bool>();
        foreach (var grp in grouped)
        {
            if (grp.Any(p => p.Status == ProcessStatus.Blocked || p.Status == ProcessStatus.InProgress))
            {
                processReady[grp.Key.ToString()] = false;
                continue;
            }
            var ready = false;
            foreach (var p in grp.Where(x => x.Status == ProcessStatus.Pending))
            {
                var item = order.Items.FirstOrDefault(i => i.Processes.Any(ip => ip.Id == p.Id));
                if (item == null) continue;
                if (IsItemProcessReady(item, p)) { ready = true; break; }
            }
            processReady[grp.Key.ToString()] = ready;
        }

        var completedProcesses = nonWithdrawn.Count(p => p.Status == ProcessStatus.Completed);
        var totalProcesses = nonWithdrawn.Count;

        var processPaused = grouped.ToDictionary(
            g => g.Key.ToString(),
            g =>
            {
                var inProgress = g.Where(p => p.Status == ProcessStatus.InProgress).ToList();
                if (inProgress.Count == 0) return false;
                // Paused only if ALL in-progress items for this process are paused
                return inProgress.All(p =>
                {
                    var hasSubs = p.SubProcesses.Any(sp => !sp.IsWithdrawn);
                    if (hasSubs)
                    {
                        var anyTimerRunning = p.SubProcesses.Any(sp => sp.Logs != null && sp.Logs.Any(l => l.EndTime == null));
                        var allDone = p.SubProcesses.Where(sp => !sp.IsWithdrawn).All(sp => sp.Status == SubProcessStatus.Completed);
                        return !anyTimerRunning && !allDone;
                    }
                    return p.PausedAt.HasValue;
                });
            });

        return new OrderMasterViewDto(
            order.Id,
            order.OrderNumber,
            order.OrderType.ToString(),
            order.Status.ToString(),
            order.DeliveryDate,
            order.Priority,
            order.CustomWarningDays,
            order.CustomCriticalDays,
            completedProcesses,
            totalProcesses,
            processStatuses,
            processDurations,
            processPaused,
            processReady,
            itemProcessReady,
            processDependencies,
            order.Attachments.Count,
            order.CreatedAt,
            order.CompletedAt,
            order.IsInvoiced);
    }

    private static ProcessStatus AggregateStatus(IGrouping<Guid, OrderItemProcess> group)
    {
        var statuses = group.Select(p => p.Status).ToList();

        if (statuses.Any(s => s == ProcessStatus.Blocked))
            return ProcessStatus.Blocked;
        if (statuses.Any(s => s == ProcessStatus.Stopped))
            return ProcessStatus.Stopped;
        if (statuses.Any(s => s == ProcessStatus.InProgress))
            return ProcessStatus.InProgress;
        if (statuses.Any(s => s == ProcessStatus.Pending))
            return ProcessStatus.Pending;
        if (statuses.All(s => s == ProcessStatus.Completed))
            return ProcessStatus.Completed;
        if (statuses.All(s => s == ProcessStatus.Withdrawn))
            return ProcessStatus.Withdrawn;

        return ProcessStatus.Completed;
    }
}
