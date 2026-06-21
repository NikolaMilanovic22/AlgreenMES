using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.ApproveBlockRequest;

public class ApproveBlockRequestCommandHandler : IRequestHandler<ApproveBlockRequestCommand, BlockRequestDto>
{
    private readonly IBlockRequestRepository _blockRequestRepository;
    private readonly IOrderItemProcessRepository _processRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;

    public ApproveBlockRequestCommandHandler(
        IBlockRequestRepository blockRequestRepository,
        IOrderItemProcessRepository processRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService eventService)
    {
        _blockRequestRepository = blockRequestRepository;
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
    }

    public async Task<BlockRequestDto> Handle(ApproveBlockRequestCommand request, CancellationToken cancellationToken)
    {
        var blockRequest = await _blockRequestRepository.GetByIdAsync(request.Id, cancellationToken);
        if (blockRequest == null)
            throw new NotFoundException("BlockRequest", request.Id);

        blockRequest.Approve(request.HandledByUserId, request.BlockReason);

        // Actually block the process
        if (blockRequest.OrderItemProcessId.HasValue)
        {
            var process = await _processRepository.GetByIdWithFullDetailsAsync(blockRequest.OrderItemProcessId.Value, cancellationToken);
            if (process != null && (process.Status == ProcessStatus.InProgress || process.Status == ProcessStatus.Pending))
            {
                // End any open time log before blocking
                var activeSub = process.SubProcesses
                    .FirstOrDefault(sp => sp.Status == SubProcessStatus.InProgress);
                if (activeSub != null)
                {
                    var openLog = activeSub.GetOpenLog();
                    if (openLog != null)
                    {
                        openLog.End();
                        if (openLog.DurationMinutes.HasValue)
                            activeSub.AddDuration(openLog.DurationMinutes.Value);
                    }
                }

                process.Block(request.HandledByUserId, request.BlockReason);
            }

            // Auto-approve other pending block requests for the same process
            var otherPending = await _blockRequestRepository.GetPendingByProcessIdAsync(
                blockRequest.OrderItemProcessId.Value, cancellationToken);
            foreach (var other in otherPending)
            {
                if (other.Id != blockRequest.Id)
                    other.Approve(request.HandledByUserId, request.BlockReason);
            }
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _eventService.NotifyBlockRequestApprovedAsync(
            new BlockRequestApprovedEvent(
                blockRequest.Id,
                blockRequest.OrderItemProcessId,
                blockRequest.OrderItemSubProcessId,
                request.BlockReason,
                blockRequest.TenantIdRequired,
                blockRequest.RequestedByUserId), cancellationToken);

        return blockRequest.Adapt<BlockRequestDto>();
    }
}
