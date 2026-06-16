using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Application.Interfaces;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Production.Application.Commands.UpdateProcess;

public class UpdateProcessCommandHandler : IRequestHandler<UpdateProcessCommand, ProcessDto>
{
    private readonly IProcessRepository _processRepository;
    private readonly IProductionUnitOfWork _unitOfWork;
    private readonly DbContext _dbContext;

    public UpdateProcessCommandHandler(IProcessRepository processRepository, IProductionUnitOfWork unitOfWork)
    {
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
        _dbContext = (DbContext)unitOfWork;
    }

    public async Task<ProcessDto> Handle(UpdateProcessCommand request, CancellationToken cancellationToken)
    {
        var process = await _processRepository.GetByIdWithSubProcessesAsync(request.Id, cancellationToken)
            ?? throw new NotFoundException("Process", request.Id);

        var codeExists = await _processRepository.ExistsByCodeAsync(request.Code, process.TenantIdRequired, request.Id, cancellationToken);
        if (codeExists)
            throw new DomainException("PROCESS_CODE_EXISTS", $"A process with code '{request.Code}' already exists.");

        process.Update(request.Code, request.Name, request.SequenceOrder);

        // Deactivate sub-processes
        if (request.DeactivateSubProcessIds is { Count: > 0 })
        {
            foreach (var subId in request.DeactivateSubProcessIds)
            {
                var sub = process.SubProcesses.FirstOrDefault(sp => sp.Id == subId)
                    ?? throw new NotFoundException("SubProcess", subId);
                sub.Deactivate();
            }
        }

        // Add new sub-processes
        if (request.AddSubProcesses is { Count: > 0 })
        {
            foreach (var item in request.AddSubProcesses)
            {
                var subProcess = process.AddSubProcess(item.Name, item.SequenceOrder);
                _dbContext.Entry(subProcess).State = EntityState.Added;
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return process.Adapt<ProcessDto>();
    }
}
