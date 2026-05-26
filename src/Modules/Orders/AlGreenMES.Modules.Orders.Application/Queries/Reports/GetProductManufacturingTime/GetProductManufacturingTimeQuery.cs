using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProductManufacturingTime;

public record GetProductManufacturingTimeQuery(
    Guid TenantId,
    DateTime? From,
    DateTime? To,
    List<OrderType>? OrderTypes,
    List<Guid>? ProductCategoryIds) : IRequest<ProductManufacturingTimeReportDto>;
