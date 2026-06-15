using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetDeliveryCompliance;

public class GetDeliveryComplianceQueryHandler
    : IRequestHandler<GetDeliveryComplianceQuery, DeliveryComplianceReportDto>
{
    private readonly IReportingQueryService _reportingQueryService;

    public GetDeliveryComplianceQueryHandler(IReportingQueryService reportingQueryService)
    {
        _reportingQueryService = reportingQueryService;
    }

    public async Task<DeliveryComplianceReportDto> Handle(
        GetDeliveryComplianceQuery request,
        CancellationToken cancellationToken)
    {
        return await _reportingQueryService.GetDeliveryComplianceAsync(
            request.TenantId,
            request.From,
            request.To,
            request.Granularity,
            request.OrderTypes,
            cancellationToken);
    }
}
