using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Production.Application.Interfaces;
using AlGreenMES.Modules.Production.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Commands.SetMaterialActive;

public class SetMaterialActiveCommandHandler : IRequestHandler<SetMaterialActiveCommand>
{
    private readonly IMaterialRepository _repo;
    private readonly IProductionUnitOfWork _unitOfWork;

    public SetMaterialActiveCommandHandler(IMaterialRepository repo, IProductionUnitOfWork unitOfWork)
    {
        _repo = repo;
        _unitOfWork = unitOfWork;
    }

    public async Task Handle(SetMaterialActiveCommand request, CancellationToken cancellationToken)
    {
        var material = await _repo.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Material", request.Id);

        if (request.IsActive) material.Reactivate();
        else material.Deactivate();

        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
