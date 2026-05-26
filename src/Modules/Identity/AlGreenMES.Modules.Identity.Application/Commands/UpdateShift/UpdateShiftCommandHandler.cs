using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.UpdateShift;

public class UpdateShiftCommandHandler : IRequestHandler<UpdateShiftCommand, ShiftDto>
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public UpdateShiftCommandHandler(IShiftRepository shiftRepository, IIdentityUnitOfWork unitOfWork)
    {
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ShiftDto> Handle(UpdateShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = await _shiftRepository.GetByIdAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Shift", request.Id);

        shift.Update(
            request.Name,
            request.StartTime,
            request.EndTime,
            request.IsActive,
            request.BreakMinutes,
            request.MaxOvertimeHours,
            request.AutoLogoutAfterHours,
            request.AlarmBeforeLogoutMinutes);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return shift.Adapt<ShiftDto>();
    }
}
