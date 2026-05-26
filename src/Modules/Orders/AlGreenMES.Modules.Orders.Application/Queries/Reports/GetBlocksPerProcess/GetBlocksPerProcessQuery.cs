using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetBlocksPerProcess;

public record GetBlocksPerProcessQuery(
    Guid TenantId,
    DateTime? From,
    DateTime? To) : IRequest<BlocksPerProcessReportDto>;
