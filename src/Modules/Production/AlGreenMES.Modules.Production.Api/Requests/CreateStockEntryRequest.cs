using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Production.Api.Requests;

public record CreateStockEntryRequest(
    StockMovementType Type,
    string DocumentReference,
    DateTime MovementDate,
    string? Notes,
    IReadOnlyList<StockEntryLineRequest> Lines);

public record StockEntryLineRequest(
    Guid MaterialId,
    decimal Quantity,
    decimal? UnitPrice,
    string? Notes);
