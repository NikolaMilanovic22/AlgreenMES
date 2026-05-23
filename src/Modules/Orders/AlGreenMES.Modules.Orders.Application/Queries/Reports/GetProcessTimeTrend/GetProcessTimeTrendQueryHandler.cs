using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProcessTimeTrend;

public class GetProcessTimeTrendQueryHandler
    : IRequestHandler<GetProcessTimeTrendQuery, ProcessTimeTrendDto>
{
    private readonly IReportingQueryService _reportingQueryService;

    public GetProcessTimeTrendQueryHandler(IReportingQueryService reportingQueryService)
    {
        _reportingQueryService = reportingQueryService;
    }

    public Task<ProcessTimeTrendDto> Handle(GetProcessTimeTrendQuery request, CancellationToken cancellationToken)
        => _reportingQueryService.GetProcessTimeTrendAsync(
            request.TenantId,
            request.ProcessId,
            request.Complexity,
            request.Granularity,
            request.From,
            request.To,
            cancellationToken);
}
