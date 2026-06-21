using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.RejectBlockRequest;

public class RejectBlockRequestCommandHandler : IRequestHandler<RejectBlockRequestCommand, BlockRequestDto>
{
    private readonly IBlockRequestRepository _blockRequestRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;

    public RejectBlockRequestCommandHandler(
        IBlockRequestRepository blockRequestRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService eventService)
    {
        _blockRequestRepository = blockRequestRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
    }

    public async Task<BlockRequestDto> Handle(RejectBlockRequestCommand request, CancellationToken cancellationToken)
    {
        var blockRequest = await _blockRequestRepository.GetByIdAsync(request.Id, cancellationToken);
        if (blockRequest == null)
            throw new NotFoundException("BlockRequest", request.Id);

        blockRequest.Reject(request.HandledByUserId, request.RejectionNote);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        await _eventService.NotifyBlockRequestRejectedAsync(
            new BlockRequestRejectedEvent(
                blockRequest.Id,
                blockRequest.RequestedByUserId,
                request.RejectionNote,
                blockRequest.TenantIdRequired), cancellationToken);

        return blockRequest.Adapt<BlockRequestDto>();
    }
}
