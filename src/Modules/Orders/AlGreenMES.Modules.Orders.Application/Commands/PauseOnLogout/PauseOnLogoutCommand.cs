using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.PauseOnLogout;

public record PauseOnLogoutCommand(Guid ProcessId, Guid TenantId, Guid UserId) : IRequest;
