using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;
using Microsoft.Extensions.Options;

namespace AlGreenMES.Modules.Orders.Application.Commands.UploadOrderAttachment;

public class UploadOrderAttachmentCommandHandler : IRequestHandler<UploadOrderAttachmentCommand, OrderAttachmentDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderAttachmentRepository _attachmentRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly FileStorageSettings _settings;

    public UploadOrderAttachmentCommandHandler(
        IOrderRepository orderRepository,
        IOrderAttachmentRepository attachmentRepository,
        IFileStorageService fileStorageService,
        IOrdersUnitOfWork unitOfWork,
        IOptions<FileStorageSettings> settings)
    {
        _orderRepository = orderRepository;
        _attachmentRepository = attachmentRepository;
        _fileStorageService = fileStorageService;
        _unitOfWork = unitOfWork;
        _settings = settings.Value;
    }

    public async Task<OrderAttachmentDto> Handle(UploadOrderAttachmentCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdAsync(request.OrderId, cancellationToken);
        if (order == null)
            throw new NotFoundException("Order", request.OrderId);

        if (order.TenantId != request.TenantId)
            throw new DomainException("FORBIDDEN", "Order does not belong to this tenant.");

        // Validate content type
        var contentType = (request.ContentType ?? "").ToLowerInvariant();
        if (!_settings.AllowedContentTypes.Contains(contentType))
            throw new DomainException("INVALID_CONTENT_TYPE", $"Content type '{request.ContentType}' is not allowed.");

        // Validate file extension (derive from content type if missing)
        var extension = Path.GetExtension(request.FileName).ToLowerInvariant();
        if (string.IsNullOrEmpty(extension))
        {
            extension = contentType switch
            {
                "image/jpeg" => ".jpg",
                "image/png" => ".png",
                "application/pdf" => ".pdf",
                _ => ""
            };
        }
        if (!_settings.AllowedExtensions.Contains(extension))
            throw new DomainException("INVALID_FILE_TYPE", $"File type '{extension}' is not allowed. Allowed: {string.Join(", ", _settings.AllowedExtensions)}");

        // Validate file size
        if (request.FileSizeBytes > _settings.MaxFileSizeBytes)
            throw new DomainException("FILE_TOO_LARGE", $"File size exceeds maximum of {_settings.MaxFileSizeBytes / (1024 * 1024)}MB.");

        // Validate item belongs to order (if item-level)
        if (request.OrderItemId.HasValue)
        {
            var itemBelongs = await _attachmentRepository.OrderItemBelongsToOrderAsync(request.OrderItemId.Value, request.OrderId, cancellationToken);
            if (!itemBelongs)
                throw new DomainException("INVALID_ORDER_ITEM", "The specified item does not belong to this order.");
        }

        // Validate count (per scope: order-level or item-level)
        int currentCount;
        if (request.OrderItemId.HasValue)
            currentCount = await _attachmentRepository.GetCountByOrderItemIdAsync(request.OrderItemId.Value, cancellationToken);
        else
            currentCount = await _attachmentRepository.GetCountByOrderIdAsync(request.OrderId, cancellationToken);
        if (currentCount >= _settings.MaxFilesPerOrder)
            throw new DomainException("TOO_MANY_ATTACHMENTS", $"Maximum of {_settings.MaxFilesPerOrder} attachments.");

        // Save file
        var storedFileName = $"{Guid.NewGuid()}{extension}";
        var relativePath = Path.Combine("orders", request.TenantId.ToString(), request.OrderId.ToString(), storedFileName);

        await _fileStorageService.SaveFileAsync(relativePath, request.FileStream, cancellationToken);

        // Create entity
        var attachment = OrderAttachment.Create(
            request.TenantId,
            request.OrderId,
            request.FileName,
            storedFileName,
            contentType,
            request.FileSizeBytes,
            relativePath,
            request.UserId,
            request.OrderItemId);

        await _attachmentRepository.AddAsync(attachment, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return attachment.Adapt<OrderAttachmentDto>();
    }
}
