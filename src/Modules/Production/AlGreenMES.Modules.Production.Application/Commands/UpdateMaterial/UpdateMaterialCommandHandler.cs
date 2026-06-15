using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Application.Interfaces;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Commands.UpdateMaterial;

public class UpdateMaterialCommandHandler : IRequestHandler<UpdateMaterialCommand, MaterialDto>
{
    private readonly IMaterialRepository _repo;
    private readonly IProductionUnitOfWork _unitOfWork;

    public UpdateMaterialCommandHandler(IMaterialRepository repo, IProductionUnitOfWork unitOfWork)
    {
        _repo = repo;
        _unitOfWork = unitOfWork;
    }

    public async Task<MaterialDto> Handle(UpdateMaterialCommand request, CancellationToken cancellationToken)
    {
        var material = await _repo.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Material", request.Id);

        material.Update(
            request.Name,
            request.Unit,
            request.Category,
            request.MinQuantity,
            request.MaxQuantity,
            request.DimensionX,
            request.DimensionY,
            request.DimensionZ,
            request.Location,
            request.Notes);
        if (request.UpdatedByUserId.HasValue)
            material.SetUpdated(request.UpdatedByUserId.Value);

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return material.Adapt<MaterialDto>();
    }
}
