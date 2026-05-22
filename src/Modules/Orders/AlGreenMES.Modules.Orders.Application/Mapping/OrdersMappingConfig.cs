using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.DTOs.Tablet;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Entities.OrderTypes;
using Mapster;

namespace AlGreenMES.Modules.Orders.Application.Mapping;

public static class OrdersMappingConfig
{
    public static void Register(TypeAdapterConfig config)
    {
        config.NewConfig<OrderType, OrderTypeDto>();

        config.NewConfig<Order, OrderDto>()
            .Map(dest => dest.ItemCount, src => src.Items.Count);

        config.NewConfig<Order, OrderDetailDto>()
            .Map(dest => dest.Items, src => src.Items)
            .Map(dest => dest.Attachments, src => src.Attachments.Where(a => a.OrderItemId == null).ToList())
            .Map(dest => dest.ManualProcesses, src => src.ManualProcesses)
            .Map(dest => dest.ManualProcessDependencies, src => src.ManualProcessDependencies);

        config.NewConfig<OrderManualProcess, OrderManualProcessDto>();
        config.NewConfig<OrderManualProcessDependency, OrderManualDependencyDto>();

        config.NewConfig<OrderItem, OrderItemDto>()
            .Map(dest => dest.Processes, src => src.Processes)
            .Map(dest => dest.SpecialRequests, src => src.SpecialRequests)
            .Map(dest => dest.Attachments, src => new List<OrderAttachmentDto>());

        config.NewConfig<OrderItemProcess, OrderItemProcessDto>()
            .Map(dest => dest.SubProcesses, src => src.SubProcesses);

        config.NewConfig<OrderItemSubProcess, OrderItemSubProcessDto>();
        config.NewConfig<OrderItemSpecialRequest, OrderItemSpecialRequestDto>();
        config.NewConfig<BlockRequest, BlockRequestDto>();
        config.NewConfig<ChangeRequest, ChangeRequestDto>();
        config.NewConfig<Notification, NotificationDto>();
        config.NewConfig<WorkSession, WorkSessionDto>();

        config.NewConfig<OrderItemProcess, TabletQueueItemDto>()
            .Map(dest => dest.OrderItemProcessId, src => src.Id)
            .Map(dest => dest.OrderId, src => src.OrderItem.Order.Id)
            .Map(dest => dest.OrderItemId, src => src.OrderItemId)
            .Map(dest => dest.OrderNumber, src => src.OrderItem.Order.OrderNumber)
            .Map(dest => dest.Priority, src => src.OrderItem.Order.Priority)
            .Map(dest => dest.DeliveryDate, src => src.OrderItem.Order.DeliveryDate)
            .Map(dest => dest.ProductName, src => src.OrderItem.ProductName)
            .Map(dest => dest.ProductCategoryName, src => (string?)null)
            .Map(dest => dest.Quantity, src => src.OrderItem.Quantity)
            .Map(dest => dest.SpecialRequestNames, src => new List<string>())
            .Map(dest => dest.CompletedProcessCount, src => 0)
            .Map(dest => dest.TotalProcessCount, src => 0)
            .Map(dest => dest.OrderNotes, src => src.OrderItem.Order.Notes)
            .Map(dest => dest.ItemNotes, src => src.OrderItem.Notes);

        config.NewConfig<OrderItemProcess, TabletActiveWorkDto>()
            .Map(dest => dest.OrderItemProcessId, src => src.Id)
            .Map(dest => dest.OrderId, src => src.OrderItem.Order.Id)
            .Map(dest => dest.OrderItemId, src => src.OrderItemId)
            .Map(dest => dest.OrderNumber, src => src.OrderItem.Order.OrderNumber)
            .Map(dest => dest.Priority, src => src.OrderItem.Order.Priority)
            .Map(dest => dest.DeliveryDate, src => src.OrderItem.Order.DeliveryDate)
            .Map(dest => dest.ProductName, src => src.OrderItem.ProductName)
            .Map(dest => dest.ProductCategoryName, src => (string?)null)
            .Map(dest => dest.Quantity, src => src.OrderItem.Quantity)
            .Map(dest => dest.SpecialRequestNames, src => new List<string>())
            .Map(dest => dest.CompletedProcessCount, src => 0)
            .Map(dest => dest.TotalProcessCount, src => 0)
            .Map(dest => dest.SubProcesses, src => src.SubProcesses)
            .Map(dest => dest.OrderNotes, src => src.OrderItem.Order.Notes)
            .Map(dest => dest.ItemNotes, src => src.OrderItem.Notes);

        config.NewConfig<OrderItemSubProcess, TabletSubProcessDto>();

        config.NewConfig<OrderItemProcess, TabletIncomingDto>()
            .Map(dest => dest.OrderItemProcessId, src => src.Id)
            .Map(dest => dest.OrderId, src => src.OrderItem.Order.Id)
            .Map(dest => dest.OrderItemId, src => src.OrderItemId)
            .Map(dest => dest.OrderNumber, src => src.OrderItem.Order.OrderNumber)
            .Map(dest => dest.Priority, src => src.OrderItem.Order.Priority)
            .Map(dest => dest.DeliveryDate, src => src.OrderItem.Order.DeliveryDate)
            .Map(dest => dest.ProductName, src => src.OrderItem.ProductName)
            .Map(dest => dest.ProductCategoryName, src => (string?)null)
            .Map(dest => dest.Quantity, src => src.OrderItem.Quantity)
            .Map(dest => dest.SpecialRequestNames, src => new List<string>())
            .Map(dest => dest.CompletedProcessCount, src => 0)
            .Map(dest => dest.TotalProcessCount, src => 0)
            .Map(dest => dest.BlockingProcesses, src => new List<BlockingProcessDto>())
            .Map(dest => dest.OrderNotes, src => src.OrderItem.Order.Notes)
            .Map(dest => dest.ItemNotes, src => src.OrderItem.Notes);

        config.NewConfig<OrderItemProcess, BlockingProcessDto>()
            .Map(dest => dest.OrderItemProcessId, src => src.Id);
    }
}
