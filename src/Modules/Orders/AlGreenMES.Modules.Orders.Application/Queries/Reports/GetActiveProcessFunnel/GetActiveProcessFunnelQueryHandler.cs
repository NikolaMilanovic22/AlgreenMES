using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetActiveProcessFunnel;

public class GetActiveProcessFunnelQueryHandler
    : IRequestHandler<GetActiveProcessFunnelQuery, ActiveProcessFunnelDto>
{
    private readonly IReportingQueryService _reportingQueryService;

    public GetActiveProcessFunnelQueryHandler(IReportingQueryService reportingQueryService)
    {
        _reportingQueryService = reportingQueryService;
    }

    public Task<ActiveProcessFunnelDto> Handle(GetActiveProcessFunnelQuery request, CancellationToken cancellationToken)
        => _reportingQueryService.GetActiveProcessFunnelAsync(
            request.TenantId,
            request.OrderTypes,
            request.Complexity,
            cancellationToken);
}
