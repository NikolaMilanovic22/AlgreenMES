using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Tenancy.Application.DTOs;
using AlGreenMES.Modules.Tenancy.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Tenancy.Application.Commands.UpdateTenantFeatures;

public class UpdateTenantFeaturesCommandHandler : IRequestHandler<UpdateTenantFeaturesCommand, TenantDto>
{
    private readonly ITenantRepository _tenantRepository;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateTenantFeaturesCommandHandler(ITenantRepository tenantRepository, IUnitOfWork unitOfWork)
    {
        _tenantRepository = tenantRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<TenantDto> Handle(UpdateTenantFeaturesCommand request, CancellationToken cancellationToken)
    {
        var tenant = await _tenantRepository.GetByIdAsync(request.TenantId, cancellationToken)
            ?? throw new NotFoundException("Tenant", request.TenantId);

        tenant.SetDisabledFeatures(request.DisabledFeatures ?? new List<string>());
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return tenant.Adapt<TenantDto>();
    }
}
