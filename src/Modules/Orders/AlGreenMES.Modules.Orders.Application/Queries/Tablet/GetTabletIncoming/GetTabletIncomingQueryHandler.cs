using AlGreenMES.Modules.Orders.Application.DTOs.Tablet;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Tablet.GetTabletIncoming;

public class GetTabletIncomingQueryHandler : IRequestHandler<GetTabletIncomingQuery, IReadOnlyList<ProcessGroupDto<TabletIncomingDto>>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly ISpecialRequestTypeRepository _specialRequestTypeRepository;
    private readonly IUserRepository _userRepository;
    private readonly IProcessRepository _processRepository;

    public GetTabletIncomingQueryHandler(
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

    public async Task<IReadOnlyList<ProcessGroupDto<TabletIncomingDto>>> Handle(GetTabletIncomingQuery request, CancellationToken cancellationToken)
    {
        var user = await _userRepository.GetByIdWithProcessesAsync(request.UserId, cancellationToken);
        if (user == null) return [];

        var userProcessIds = user.GetProcessIds();
        if (userProcessIds.Count == 0) return [];

        var orders = await _orderRepository.GetActiveOrdersWithProcessesAsync(request.TenantId, cancellationToken);

        var specialRequestTypes = await _specialRequestTypeRepository.GetByTenantIdAsync(request.TenantId, cancellationToken);
        var srLookup = specialRequestTypes.ToDictionary(s => s.Id, s => s.Name);

        var result = new List<ProcessGroupDto<TabletIncomingDto>>();

        foreach (var processId in userProcessIds)
        {
            var prodProcess = await _processRepository.GetByIdAsync(processId, cancellationToken);
            if (prodProcess == null) continue;

            var items = new List<TabletIncomingDto>();

            foreach (var order in orders)
            {
                var manualDepsByProcess = order.HasManualProcesses
                    ? order.ManualProcessDependencies
                        .GroupBy(d => d.ProcessId)
                        .ToDictionary(g => g.Key, g => g.Select(d => d.DependsOnProcessId).ToList())
                    : null;

                foreach (var item in order.Items)
                {
                    var category = await _categoryRepository.GetByIdWithDetailsAsync(item.ProductCategoryId, cancellationToken);

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

                        var blockingProcesses = new List<BlockingProcessDto>();
                        foreach (var depProcessId in processDeps)
                        {
                            var depProcess = item.Processes.FirstOrDefault(p => p.ProcessId == depProcessId);
                            if (depProcess != null && depProcess.Status != ProcessStatus.Completed && depProcess.Status != ProcessStatus.Withdrawn)
                            {
                                blockingProcesses.Add(depProcess.Adapt<BlockingProcessDto>());
                            }
                        }

                        if (blockingProcesses.Count == 0) continue;

                        var dto = process.Adapt<TabletIncomingDto>() with
                        {
                            ProductCategoryName = category?.Name,
                            SpecialRequestNames = specialRequestNames,
                            CompletedProcessCount = completedCount,
                            TotalProcessCount = totalCount,
                            BlockingProcesses = blockingProcesses
                        };
                        items.Add(dto);
                    }
                }
            }

            result.Add(new ProcessGroupDto<TabletIncomingDto>(
                prodProcess.Id, prodProcess.Code, prodProcess.Name, prodProcess.SequenceOrder, items));
        }

        return result.OrderBy(g => g.SequenceOrder).ToList();
    }
}
