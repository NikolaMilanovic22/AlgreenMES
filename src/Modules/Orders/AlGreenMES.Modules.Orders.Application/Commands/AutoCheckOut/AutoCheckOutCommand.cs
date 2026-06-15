using AlGreenMES.Modules.Orders.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.AutoCheckOut;

// Bojan 30.05.2026 follow-up — fired by the tablet (and the server-side lazy
// safety net) when the auto-logout cap expires. Same close-out as CheckOut but
// marks WasAutoClosed=true so the tablet shows the auto-logout screen and the
// coordinator gets a warning. The cap moment is passed as `When` so the
// recorded checkout matches when the cap actually expired (not when the server
// processed it).
public record AutoCheckOutCommand(Guid UserId, DateTime? When = null) : IRequest<WorkSessionDto>;
