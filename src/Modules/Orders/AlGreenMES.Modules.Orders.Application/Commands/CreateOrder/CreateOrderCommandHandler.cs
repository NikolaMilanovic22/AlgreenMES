using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;
using Microsoft.Extensions.Options;

namespace AlGreenMES.Modules.Orders.Application.Commands.CreateOrder;

public class CreateOrderCommandHandler : IRequestHandler<CreateOrderCommand, OrderDetailDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IProcessRepository _processRepository;
    private readonly IOrderAttachmentRepository _attachmentRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly FileStorageSettings _settings;

    public CreateOrderCommandHandler(
        IOrderRepository orderRepository,
        IProductCategoryRepository categoryRepository,
        IProcessRepository processRepository,
        IOrderAttachmentRepository attachmentRepository,
        IFileStorageService fileStorageService,
        IOrdersUnitOfWork unitOfWork,
        IOptions<FileStorageSettings> settings)
    {
        _orderRepository = orderRepository;
        _categoryRepository = categoryRepository;
        _processRepository = processRepository;
        _attachmentRepository = attachmentRepository;
        _fileStorageService = fileStorageService;
        _unitOfWork = unitOfWork;
        _settings = settings.Value;
    }

    public async Task<OrderDetailDto> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        var exists = await _orderRepository.ExistsByOrderNumberAsync(request.OrderNumber, request.TenantId, cancellationToken);
        if (exists)
            throw new DomainException("ORDER_NUMBER_EXISTS", $"An order with number '{request.OrderNumber}' already exists.");

        var order = Order.Create(
            request.TenantId,
            request.OrderNumber,
            request.DeliveryDate,
            request.Priority,
            request.OrderType,
            request.CreatedByUserId,
            request.Notes);

        if (request.CustomWarningDays.HasValue || request.CustomCriticalDays.HasValue)
            order.SetCustomWarningDays(request.CustomWarningDays, request.CustomCriticalDays);

        // Manual processes — store on the order. When present, these REPLACE the
        // per-item product-category processes during item creation below.
        var hasManual = request.ManualProcesses is { Count: > 0 };
        if (hasManual)
        {
            foreach (var mp in request.ManualProcesses!)
                order.AddManualProcess(mp.ProcessId, mp.SequenceOrder, mp.DefaultComplexity);

            if (request.ManualDependencies is { Count: > 0 })
            {
                foreach (var dep in request.ManualDependencies)
                    order.AddManualDependency(dep.ProcessId, dep.DependsOnProcessId);
            }
        }

        // Add items if provided
        if (request.Items is { Count: > 0 })
        {
            foreach (var itemInput in request.Items)
            {
                var category = await _categoryRepository.GetByIdWithDetailsAsync(itemInput.ProductCategoryId, cancellationToken)
                    ?? throw new NotFoundException("ProductCategory", itemInput.ProductCategoryId);

                var item = order.AddItem(itemInput.ProductCategoryId, itemInput.ProductName, itemInput.Quantity, itemInput.Notes);

                // Pick the process source: manual list (if order's type allows + provided)
                // or the product category's processes. The manual list is per-order, not
                // per-item, so each item gets the same set.
                IEnumerable<(Guid ProcessId, ComplexityType? Complexity)> processSource = hasManual
                    ? order.ManualProcesses
                        .OrderBy(p => p.SequenceOrder)
                        .Select(p => (p.ProcessId, p.DefaultComplexity))
                    : category.Processes
                        .OrderBy(p => p.SequenceOrder)
                        .Select(p => (p.ProcessId, p.DefaultComplexity));

                foreach (var (processId, complexity) in processSource)
                {
                    var oip = item.AddProcess(processId, complexity);

                    var process = await _processRepository.GetByIdWithSubProcessesAsync(processId, cancellationToken);
                    if (process?.SubProcesses != null)
                    {
                        foreach (var sub in process.SubProcesses.Where(s => s.IsActive).OrderBy(s => s.SequenceOrder))
                        {
                            oip.AddSubProcess(sub.Id);
                        }
                    }
                }

                _orderRepository.AddItem(item);

                // Process per-item attachments
                if (itemInput.Attachments is { Count: > 0 })
                {
                    foreach (var file in itemInput.Attachments)
                        await SaveAttachment(file, request.TenantId, order.Id, request.CreatedByUserId, item.Id, cancellationToken);
                }
            }
        }

        await _orderRepository.AddAsync(order, cancellationToken);

        // Process order-level attachments
        if (request.Attachments is { Count: > 0 })
        {
            foreach (var file in request.Attachments)
                await SaveAttachment(file, request.TenantId, order.Id, request.CreatedByUserId, null, cancellationToken);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return order.Adapt<OrderDetailDto>();
    }

    private async Task SaveAttachment(CreateOrderAttachmentInput file, Guid tenantId, Guid orderId, Guid userId, Guid? orderItemId, CancellationToken cancellationToken)
    {
        var contentType = (file.ContentType ?? "").ToLowerInvariant();
        if (!_settings.AllowedContentTypes.Contains(contentType))
            throw new DomainException("INVALID_CONTENT_TYPE", $"Content type '{file.ContentType}' is not allowed.");

        var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
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

        if (file.FileSizeBytes > _settings.MaxFileSizeBytes)
            throw new DomainException("FILE_TOO_LARGE", $"File size exceeds maximum of {_settings.MaxFileSizeBytes / (1024 * 1024)}MB.");

        var storedFileName = $"{Guid.NewGuid()}{extension}";
        var relativePath = Path.Combine("orders", tenantId.ToString(), orderId.ToString(), storedFileName);

        await _fileStorageService.SaveFileAsync(relativePath, file.FileStream, cancellationToken);

        var attachment = OrderAttachment.Create(
            tenantId,
            orderId,
            file.FileName,
            storedFileName,
            file.ContentType,
            file.FileSizeBytes,
            relativePath,
            userId,
            orderItemId);

        await _attachmentRepository.AddAsync(attachment, cancellationToken);
    }
}
