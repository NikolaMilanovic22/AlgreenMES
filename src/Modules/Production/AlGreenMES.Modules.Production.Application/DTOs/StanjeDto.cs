namespace AlGreenMES.Modules.Production.Application.DTOs;

/// <summary>Status zaliha — derived from quantity vs material thresholds.</summary>
public enum StockStatus
{
    Ok,
    IspodMin,
    IznadMax
}

public record StanjeRowDto(
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
