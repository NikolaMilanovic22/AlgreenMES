using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetBlocksPerProcess;

public class GetBlocksPerProcessQueryHandler
    : IRequestHandler<GetBlocksPerProcessQuery, BlocksPerProcessReportDto>
{
    private readonly IReportingQueryService _reportingQueryService;

    public GetBlocksPerProcessQueryHandler(IReportingQueryService reportingQueryService)
    {
        _reportingQueryService = reportingQueryService;
    }

    public Task<BlocksPerProcessReportDto> Handle(GetBlocksPerProcessQuery request, CancellationToken cancellationToken)
        => _reportingQueryService.GetBlocksPerProcessAsync(
            request.TenantId,
            request.From,
            request.To,
            cancellationToken);
}
