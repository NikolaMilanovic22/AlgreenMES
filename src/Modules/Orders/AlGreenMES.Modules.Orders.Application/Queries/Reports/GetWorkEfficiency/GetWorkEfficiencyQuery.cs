using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetWorkEfficiency;

public record GetWorkEfficiencyQuery(
    Guid TenantId,
    DateOnly From,
    DateOnly To,
    Guid? UserId) : IRequest<WorkEfficiencyReportDto>;
