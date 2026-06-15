using MediatR;

namespace AlGreenMES.Modules.Production.Application.Commands.SetMaterialActive;

public record SetMaterialActiveCommand(Guid Id, bool IsActive) : IRequest;
