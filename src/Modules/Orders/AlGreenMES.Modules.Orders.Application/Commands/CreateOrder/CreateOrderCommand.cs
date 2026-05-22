using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.CreateOrder;

public record CreateOrderItemInput(Guid ProductCategoryId, string ProductName, int Quantity, string? Notes, List<CreateOrderAttachmentInput>? Attachments = null);
public record CreateOrderAttachmentInput(string FileName, string ContentType, long FileSizeBytes, Stream FileStream);

public record CreateOrderManualProcessInput(Guid ProcessId, int SequenceOrder, ComplexityType? DefaultComplexity);
public record CreateOrderManualDependencyInput(Guid ProcessId, Guid DependsOnProcessId);

public record CreateOrderCommand(
    Guid TenantId,
    string OrderNumber,
    DateTime DeliveryDate,
    int Priority,
    OrderType OrderType,
    Guid CreatedByUserId,
    string? Notes,
    int? CustomWarningDays = null,
    int? CustomCriticalDays = null,
    List<CreateOrderItemInput>? Items = null,
    List<CreateOrderAttachmentInput>? Attachments = null,
    List<CreateOrderManualProcessInput>? ManualProcesses = null,
    List<CreateOrderManualDependencyInput>? ManualDependencies = null) : IRequest<OrderDetailDto>;
