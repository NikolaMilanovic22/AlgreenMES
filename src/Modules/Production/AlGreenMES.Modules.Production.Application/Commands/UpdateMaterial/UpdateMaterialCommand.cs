using AlGreenMES.Modules.Production.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Commands.UpdateMaterial;

public record UpdateMaterialCommand(
    Guid Id,
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
    Guid? UpdatedByUserId) : IRequest<MaterialDto>;
