using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.ResumeOnLogin;

public record ResumeOnLoginCommand(Guid ProcessId, Guid TenantId, Guid UserId) : IRequest;
