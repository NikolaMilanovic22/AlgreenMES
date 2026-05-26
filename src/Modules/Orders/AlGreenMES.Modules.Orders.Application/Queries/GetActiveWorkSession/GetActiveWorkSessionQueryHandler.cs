using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.GetActiveWorkSession;

public class GetActiveWorkSessionQueryHandler
    : IRequestHandler<GetActiveWorkSessionQuery, ActiveWorkSessionDto?>
{
    private readonly IReportingQueryService _reportingQueryService;

    public GetActiveWorkSessionQueryHandler(IReportingQueryService reportingQueryService)
    {
        _reportingQueryService = reportingQueryService;
    }

    public Task<ActiveWorkSessionDto?> Handle(
        GetActiveWorkSessionQuery request,
        CancellationToken cancellationToken)
        => _reportingQueryService.GetActiveWorkSessionAsync(
            request.TenantId,
            request.UserId,
            cancellationToken);
}
