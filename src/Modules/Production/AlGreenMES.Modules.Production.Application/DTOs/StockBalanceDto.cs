namespace AlGreenMES.Modules.Production.Application.DTOs;

/// <summary>Stock status — derived from quantity vs material thresholds.</summary>
public enum StockStatus
{
    Ok,
    BelowMin,
    AboveMax
}

public record StockBalanceRowDto(
    Guid MaterialId,
    string Code,
    string Name,
    string Unit,
    string Category,
    decimal? DimensionX,
    decimal? DimensionY,
    decimal? DimensionZ,
    decimal Quantity,
    decimal LatestUnitPrice,
    decimal TotalValue,
    int MinQuantity,
    int MaxQuantity,
    StockStatus Status,
    string? Location,
    string? Notes);
