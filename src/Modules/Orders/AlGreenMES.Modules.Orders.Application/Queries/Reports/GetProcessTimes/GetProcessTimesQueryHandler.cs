using AlGreenMES.Modules.Orders.Application.DTOs.Reports;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.Reports.GetProcessTimes;

public class GetProcessTimesQueryHandler : IRequestHandler<GetProcessTimesQuery, ProcessTimesDto>
{
    private readonly IReportingQueryService _reportingQueryService;

    public GetProcessTimesQueryHandler(IReportingQueryService reportingQueryService)
    {
        _reportingQueryService = reportingQueryService;
    }

    public async Task<ProcessTimesDto> Handle(GetProcessTimesQuery request, CancellationToken cancellationToken)
    {
        return await _reportingQueryService.GetProcessTimesAsync(
            request.TenantId,
            request.From,
            request.To,
            request.ProductCategoryIds,
            request.OrderTypes,
            cancellationToken);
    }
}
