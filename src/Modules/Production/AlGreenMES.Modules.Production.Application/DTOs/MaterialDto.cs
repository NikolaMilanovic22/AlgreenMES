namespace AlGreenMES.Modules.Production.Application.DTOs;

public record MaterialDto(
    Guid Id,
    string Code,
    string Name,
    string Unit,
    string Category,
    int MinQuantity,
    int MaxQuantity,
    decimal? DimensionX,
    decimal? DimensionY,
    decimal? DimensionZ,
    string? Location,
    string? Notes,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
