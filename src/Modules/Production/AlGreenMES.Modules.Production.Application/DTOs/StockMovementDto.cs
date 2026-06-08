using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Production.Application.DTOs;

public record StockMovementDto(
    Guid Id,
    Guid MaterialId,
    string MaterialCode,
    string MaterialName,
    string Unit,
    string Category,
    decimal? DimensionX,
    decimal? DimensionY,
    decimal? DimensionZ,
    StockMovementType Type,
    decimal Quantity,
    decimal UnitPrice,
    decimal TotalPrice,
    DateTime MovementDate,
    string DocumentReference,
    string? Notes,
    DateTime CreatedAt);
