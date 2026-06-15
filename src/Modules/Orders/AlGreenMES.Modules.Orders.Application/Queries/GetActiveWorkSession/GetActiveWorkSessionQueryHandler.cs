using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.Commands.AutoCheckOut;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Queries.GetActiveWorkSession;

public class GetActiveWorkSessionQueryHandler
    : IRequestHandler<GetActiveWorkSessionQuery, ActiveWorkSessionDto?>
{
    private readonly IReportingQueryService _reportingQueryService;
    private readonly IMediator _mediator;

    public GetActiveWorkSessionQueryHandler(IReportingQueryService reportingQueryService, IMediator mediator)
    {
        _reportingQueryService = reportingQueryService;
        _mediator = mediator;
    }

    public async Task<ActiveWorkSessionDto?> Handle(
        GetActiveWorkSessionQuery request,
        CancellationToken cancellationToken)
    {
        var result = await _reportingQueryService.GetActiveWorkSessionAsync(
            request.TenantId,
            request.UserId,
            cancellationToken);

        // Lazy server-side safety net (Bojan 30.05.2026): if the tablet went
        // offline / the worker walked away and the auto-logout cap has already
        // expired, close the session server-side so the next poll sees "no
        // active session" rather than a phantom open one. The recorded
        // checkout time backdates to logoutAt — when the cap actually expired.
        if (result?.LogoutAtUtc is DateTime logoutAt && logoutAt <= DateTime.UtcNow)
        {
            // Race: a concurrent poll may have already closed the session.
            // AutoCheckOut throws ALREADY_CHECKED_OUT in that case — ignore.
            try
            {
                await _mediator.Send(new AutoCheckOutCommand(request.UserId, logoutAt), cancellationToken);
            }
            catch (DomainException)
            {
                // Already closed by a concurrent poll — fine.
            }
            return null;
        }

        return result;
    }
}
