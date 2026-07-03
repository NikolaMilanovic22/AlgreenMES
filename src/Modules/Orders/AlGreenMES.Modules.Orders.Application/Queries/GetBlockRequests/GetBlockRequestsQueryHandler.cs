using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.GetBlockRequests;

public class GetBlockRequestsQueryHandler : IRequestHandler<GetBlockRequestsQuery, PagedResult<BlockRequestDto>>
{
    private readonly IBlockRequestRepository _blockRequestRepository;
    private readonly IProcessRepository _processRepository;

    public GetBlockRequestsQueryHandler(IBlockRequestRepository blockRequestRepository, IProcessRepository processRepository)
    {
        _blockRequestRepository = blockRequestRepository;
        _processRepository = processRepository;
    }

    public async Task<PagedResult<BlockRequestDto>> Handle(GetBlockRequestsQuery request, CancellationToken cancellationToken)
    {
        var result = await _blockRequestRepository.GetPagedAsync(
            request.TenantId, request.Status, request.OrderId, request.Search,
            request.GetCreatedFromUtc(), request.GetCreatedToUtc(),
            request.SortBy, request.IsDescending,
            request.GetPage(), request.GetPageSize(), cancellationToken);

        // Batch-load process names
        var processIds = result.Items
            .Where(b => b.OrderItemProcess?.ProcessId != null)
            .Select(b => b.OrderItemProcess!.ProcessId)
            .Distinct()
            .ToList();
        var processNames = (await _processRepository.GetByIdsAsync(processIds, cancellationToken))
            .ToDictionary(p => p.Id, p => p.Name);

        return result.MapItems(b =>
        {
            var order = b.OrderItemProcess?.OrderItem?.Order;
            var processId = b.OrderItemProcess?.ProcessId;
            var dto = b.Adapt<BlockRequestDto>();
            return dto with {
                OrderId = order?.Id,
                OrderNumber = order?.OrderNumber,
                CurrentProcessStatus = b.OrderItemProcess?.Status,
                ProcessId = processId,
                ProcessName = processId.HasValue && processNames.TryGetValue(processId.Value, out var name) ? name : null,
            };
        });
    }
}
