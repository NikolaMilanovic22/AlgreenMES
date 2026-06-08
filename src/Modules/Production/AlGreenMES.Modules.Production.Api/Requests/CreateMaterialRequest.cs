namespace AlGreenMES.Modules.Production.Api.Requests;

public record CreateMaterialRequest(
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
    string? Notes);
