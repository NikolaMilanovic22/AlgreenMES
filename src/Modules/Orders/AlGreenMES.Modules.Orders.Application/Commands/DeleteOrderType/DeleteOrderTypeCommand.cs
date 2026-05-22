using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.DeleteOrderType;

/// <summary>
/// Result mirrors the smart-delete pattern from Process/ProductCategory:
/// HardDeleted=true → row removed; Deactivated=true → row kept, IsActive=false (in-use case).
/// </summary>
public record DeleteOrderTypeResult(bool HardDeleted, bool Deactivated);

public record DeleteOrderTypeCommand(Guid Id) : IRequest<DeleteOrderTypeResult>;
