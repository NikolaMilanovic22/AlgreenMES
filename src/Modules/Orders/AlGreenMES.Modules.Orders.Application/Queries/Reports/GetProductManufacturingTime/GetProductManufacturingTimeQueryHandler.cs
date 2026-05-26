using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProductManufacturingTime;

public class GetProductManufacturingTimeQueryHandler
    : IRequestHandler<GetProductManufacturingTimeQuery, ProductManufacturingTimeReportDto>
{
    private readonly IReportingQueryService _reportingQueryService;

    public GetProductManufacturingTimeQueryHandler(IReportingQueryService reportingQueryService)
    {
        _reportingQueryService = reportingQueryService;
    }

    public Task<ProductManufacturingTimeReportDto> Handle(
        GetProductManufacturingTimeQuery request,
        CancellationToken cancellationToken)
        => _reportingQueryService.GetProductManufacturingTimeAsync(
            request.TenantId,
            request.From,
            request.To,
            request.OrderTypes,
            request.ProductCategoryIds,
            cancellationToken);
}
