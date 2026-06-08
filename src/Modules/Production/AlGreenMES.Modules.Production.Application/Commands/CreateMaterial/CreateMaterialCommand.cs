using AlGreenMES.Modules.Production.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Commands.CreateMaterial;

public record CreateMaterialCommand(
    Guid TenantId,
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
    Guid? CreatedByUserId) : IRequest<MaterialDto>;
