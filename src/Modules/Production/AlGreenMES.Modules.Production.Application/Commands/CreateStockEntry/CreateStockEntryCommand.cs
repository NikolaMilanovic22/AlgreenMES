using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Commands.CreateStockEntry;

/// <summary>
/// Single command for both Ulaz and Izlaz — Excel sheets are structurally
/// identical, only Type + document reference label differ. One header
/// (DocumentReference + MovementDate) + N material lines.
/// </summary>
public record CreateStockEntryCommand(
    Guid TenantId,
    StockMovementType Type,
    string DocumentReference,
    DateTime MovementDate,
    string? Notes,
    IReadOnlyList<StockEntryLine> Lines,
    Guid? CreatedByUserId,
    Guid? ProcessId = null) : IRequest<IReadOnlyList<StockMovementDto>>;

public record StockEntryLine(
    Guid MaterialId,
    decimal Quantity,
    decimal? UnitPrice,
    string? Notes);
