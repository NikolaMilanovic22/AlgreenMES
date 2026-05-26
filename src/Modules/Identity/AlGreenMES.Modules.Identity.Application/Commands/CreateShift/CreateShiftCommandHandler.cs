using AlGreenMES.Modules.Identity.Application.DTOs;
using AlGreenMES.Modules.Identity.Application.Interfaces;
using AlGreenMES.Modules.Identity.Domain.Entities;
using AlGreenMES.Modules.Identity.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Identity.Application.Commands.CreateShift;

public class CreateShiftCommandHandler : IRequestHandler<CreateShiftCommand, ShiftDto>
{
    private readonly IShiftRepository _shiftRepository;
    private readonly IIdentityUnitOfWork _unitOfWork;

    public CreateShiftCommandHandler(IShiftRepository shiftRepository, IIdentityUnitOfWork unitOfWork)
    {
        _shiftRepository = shiftRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<ShiftDto> Handle(CreateShiftCommand request, CancellationToken cancellationToken)
    {
        var shift = Shift.Create(
            request.TenantId,
            request.Name,
            request.StartTime,
            request.EndTime,
            request.BreakMinutes,
            request.MaxOvertimeHours,
            request.AutoLogoutAfterHours,
            request.AlarmBeforeLogoutMinutes);

        await _shiftRepository.AddAsync(shift, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return shift.Adapt<ShiftDto>();
    }
}
