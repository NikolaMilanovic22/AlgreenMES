using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Orders.Application.Commands.DeleteAllNotifications;
using AlGreenMES.Modules.Orders.Application.Commands.DeleteNotification;
using AlGreenMES.Modules.Orders.Application.Commands.MarkAllNotificationsRead;
using AlGreenMES.Modules.Orders.Application.Commands.MarkNotificationRead;
using AlGreenMES.Modules.Orders.Application.Commands.MarkNotificationUnread;
using AlGreenMES.Modules.Orders.Application.Queries.GetNotifications;
using AlGreenMES.Modules.Orders.Application.Queries.GetUnreadNotificationCount;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ICurrentUserService _currentUser;

    public NotificationsController(IMediator mediator, ICurrentUserService currentUser)
    {
        _mediator = mediator;
        _currentUser = currentUser;
    }

    [HttpGet]
    public async Task<IActionResult> GetNotifications(
        [FromQuery] bool? isRead,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetNotificationsQuery
        {
            UserId = _currentUser.GetCurrentUserId(),
            IsRead = isRead,
            Page = page,
            PageSize = pageSize,
            Search = search
        }, cancellationToken);
        return Ok(result);
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetUnreadNotificationCountQuery(_currentUser.GetCurrentUserId()),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new MarkNotificationReadCommand(id, _currentUser.GetCurrentUserId()), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/unread")]
    public async Task<IActionResult> MarkAsUnread(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new MarkNotificationUnreadCommand(id, _currentUser.GetCurrentUserId()), cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        await _mediator.Send(new MarkAllNotificationsReadCommand(_currentUser.GetCurrentUserId()), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteNotificationCommand(id, _currentUser.GetCurrentUserId()), cancellationToken);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> DeleteAll(CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteAllNotificationsCommand(_currentUser.GetCurrentUserId()), cancellationToken);
        return NoContent();
    }
}
