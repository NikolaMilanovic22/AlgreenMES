using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Queries.GetMaterial;

public class GetMaterialQueryHandler : IRequestHandler<GetMaterialQuery, MaterialDto?>
{
    private readonly IMaterialRepository _repo;

    public GetMaterialQueryHandler(IMaterialRepository repo)
    {
        _repo = repo;
    }

    public async Task<MaterialDto?> Handle(GetMaterialQuery request, CancellationToken cancellationToken)
    {
        var m = await _repo.GetByIdAsync(request.Id, cancellationToken);
        return m?.Adapt<MaterialDto>();
    }
}
