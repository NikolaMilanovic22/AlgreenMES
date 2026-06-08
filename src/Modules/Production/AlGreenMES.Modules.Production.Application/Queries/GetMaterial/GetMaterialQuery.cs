using AlGreenMES.Modules.Production.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Queries.GetMaterial;

public record GetMaterialQuery(Guid Id) : IRequest<MaterialDto?>;
