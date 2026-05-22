using AlGreenMES.Modules.Orders.Application.DTOs.Tablet;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Tablet.GetTabletQueue;

public class GetTabletQueueQueryHandler : IRequestHandler<GetTabletQueueQuery, IReadOnlyList<ProcessGroupDto<TabletQueueItemDto>>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly ISpecialRequestTypeRepository _specialRequestTypeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IProcessRepository _processRepository;

    public GetTabletQueueQueryHandler(
        IOrderRepository orderRepository,
        IProductCategoryRepository categoryRepository,
        ISpecialRequestTypeRepository specialRequestTypeRepository,
        IUserRepository userRepository,
        IProcessRepository processRepository)
    {
        _orderRepository = orderRepository;
        _categoryRepository = categoryRepository;
        _specialRequestTypeRepository = specialRequestTypeRepository;
        _userRepository = userRepository;
        _processRepository = processRepository;
    }

    public async Task<IReadOnlyList<ProcessGroupDto<TabletQueueItemDto>>> Handle(GetTabletQueueQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdWithProcessesAsync(request.UserId, cancellationToken);
        if (user == null) return [];

        var userProcessIds = user.GetProcessIds();
        if (userProcessIds.Count == 0) return [];

        var orders = await _orderRepository.GetActiveOrdersWithProcessesAsync(request.TenantId, cancellationToken);

        var specialRequestTypes = await _specialRequestTypeRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);
        var srLookup = specialRequestTypes.ToDictionary(s => s.Id, s => s.Name);

        // Batch-load processes + categories once instead of per-iteration N+1
        // queries (Sentry flagged this endpoint with ~22 N+1 hits/week).
        var prodProcessLookup = (await _processRepository.GetByIdsAsync(userProcessIds, cancellationToken))
            .ToDictionary(p => p.Id);
        var categoryIds = orders.SelectMany(o => o.Items).Select(i => i.ProductCategoryId).Distinct().ToList();
        var categoryLookup = (await _categoryRepository.GetByIdsWithDetailsAsync(categoryIds, cancellationToken))
            .ToDictionary(c => c.Id);

        var result = new List<ProcessGroupDto<TabletQueueItemDto>>();

        foreach (var processId in userProcessIds)
        {
            if (!prodProcessLookup.TryGetValue(processId, out var prodProcess)) continue;

            var items = new List<TabletQueueItemDto>();

            foreach (var order in orders)
            {
                // Manual processes override the category dependency graph entirely
                // when present (item.Processes only contain the manual list, so
                // category-driven deps would reference processes the item doesn't
                // have and `allDepsCompleted` would always be false).
                var manualDepsByProcess = order.HasManualProcesses
                    ? order.ManualProcessDependencies
                        .GroupBy(d => d.ProcessId)
                        .ToDictionary(g => g.Key, g => g.Select(d => d.DependsOnProcessId).ToList())
                    : null;

                foreach (var item in order.Items)
                {
                    categoryLookup.TryGetValue(item.ProductCategoryId, out var category);

                    var specialRequestNames = item.SpecialRequests
                        .Select(sr => srLookup.GetValueOrDefault(sr.SpecialRequestTypeId, ""))
                        .Where(name => !string.IsNullOrEmpty(name))
                        .ToList();

                    var completedCount = item.Processes.Count(p =>
                        p.Status == ProcessStatus.Completed || p.Status == ProcessStatus.Withdrawn);
                    var totalCount = item.Processes.Count(p => !p.IsWithdrawn);

                    foreach (var process in item.Processes)
                    {
                        if (process.ProcessId != processId) continue;
                        if (process.Status != ProcessStatus.Pending) continue;
                        if (process.IsWithdrawn) continue;

                        List<Guid> processDeps;
                        if (manualDepsByProcess is not null)
                        {
                            processDeps = manualDepsByProcess.GetValueOrDefault(process.ProcessId) ?? new List<Guid>();
                        }
                        else
                        {
                            processDeps = (category?.Dependencies ?? [])
                                .Where(d => d.ProcessId == process.ProcessId)
                                .Select(d => d.DependsOnProcessId)
                                .ToList();
                        }

                        var allDepsCompleted = processDeps.All(depProcessId =>
                            item.Processes.Any(p =>
                                p.ProcessId == depProcessId &&
                                (p.Status == ProcessStatus.Completed || p.Status == ProcessStatus.Withdrawn)));

                        if (!allDepsCompleted) continue;

                        var duration = process.SubProcesses.Any(sp => !sp.IsWithdrawn)
                            ? process.SubProcesses.Sum(sp => sp.TotalDurationMinutes)
                            : process.TotalDurationMinutes;
                        var dto = process.Adapt<TabletQueueItemDto>() with
                        {
                            ProductCategoryName = category?.Name,
                            SpecialRequestNames = specialRequestNames,
                            CompletedProcessCount = completedCount,
                            TotalProcessCount = totalCount,
                            TotalDurationMinutes = duration
                        };
                        items.Add(dto);
                    }
                }
            }

            items = items
                .OrderBy(x => x.Priority)
                .ThenBy(x => x.DeliveryDate)
                .ToList();

            result.Add(new ProcessGroupDto<TabletQueueItemDto>(
                prodProcess.Id, prodProcess.Code, prodProcess.Name, prodProcess.SequenceOrder, items));
        }

        return result.OrderBy(g => g.SequenceOrder).ToList();
    }
}
