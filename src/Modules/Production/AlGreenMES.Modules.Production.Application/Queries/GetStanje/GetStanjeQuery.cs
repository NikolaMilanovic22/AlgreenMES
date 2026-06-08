using AlGreenMES.Modules.Production.Application.DTOs;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Queries.GetStanje;

public record GetStanjeQuery(Guid TenantId) : IRequest<IReadOnlyList<StanjeRowDto>>;
