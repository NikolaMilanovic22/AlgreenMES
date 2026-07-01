using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Application.DTOs.Tablet;

public record TabletIncomingDto(
    Guid OrderItemProcessId,
    Guid OrderId,
    Guid OrderItemId,
    string OrderNumber,
    int Priority,
    DateTime DeliveryDate,
    string ProductName,
    string? ProductCategoryName,
    int Quantity,
    ComplexityType? Complexity,
    ProcessStatus Status,
    List<string> SpecialRequestNames,
    int CompletedProcessCount,
    int TotalProcessCount,
    List<BlockingProcessDto> BlockingProcesses,
    string? OrderNotes,
    string? ItemNotes,
    int AttachmentCount = 0);

public record BlockingProcessDto(
    Guid OrderItemProcessId,
    Guid ProcessId,
    ProcessStatus Status);
