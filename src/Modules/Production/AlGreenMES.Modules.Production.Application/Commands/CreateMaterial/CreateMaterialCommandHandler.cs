using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Application.Interfaces;
using AlGreenMES.Modules.Production.Domain.Entities;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Commands.CreateMaterial;

public class CreateMaterialCommandHandler : IRequestHandler<CreateMaterialCommand, MaterialDto>
{
    private readonly IMaterialRepository _repo;
    private readonly IProductionUnitOfWork _unitOfWork;

    public CreateMaterialCommandHandler(IMaterialRepository repo, IProductionUnitOfWork unitOfWork)
    {
        _repo = repo;
        _unitOfWork = unitOfWork;
    }

    public async Task<MaterialDto> Handle(CreateMaterialCommand request, CancellationToken cancellationToken)
    {
        if (await _repo.ExistsByKodAsync(request.Code, request.TenantId, excludingId: null, cancellationToken))
            throw new DomainException("MATERIAL_KOD_EXISTS",
                $"Materijal sa kodom '{request.Code}' već postoji.");

        var material = Material.Create(
            request.TenantId,
            request.Code,
            request.Name,
            request.Unit,
            request.Category,
            request.MinQuantity,
            request.MaxQuantity,
            request.DimensionX,
            request.DimensionY,
            request.DimensionZ,
            request.Location,
            request.Notes,
            request.CreatedByUserId);

        await _repo.AddAsync(material, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return material.Adapt<MaterialDto>();
    }
}
