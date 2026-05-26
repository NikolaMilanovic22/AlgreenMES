using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetWorkEfficiency;

public class GetWorkEfficiencyQueryHandler
    : IRequestHandler<GetWorkEfficiencyQuery, WorkEfficiencyReportDto>
{
    private readonly IReportingQueryService _reportingQueryService;

    public GetWorkEfficiencyQueryHandler(IReportingQueryService reportingQueryService)
    {
        _reportingQueryService = reportingQueryService;
    }

    public Task<WorkEfficiencyReportDto> Handle(
        GetWorkEfficiencyQuery request,
        CancellationToken cancellationToken)
        => _reportingQueryService.GetWorkEfficiencyAsync(
            request.TenantId,
            request.From,
            request.To,
            request.UserId,
            cancellationToken);
}
