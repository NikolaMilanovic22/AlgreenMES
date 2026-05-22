using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;

namespace AlGreenMES.Modules.Orders.Application.Commands.AddOrderItem;

public class AddOrderItemCommandHandler : IRequestHandler<AddOrderItemCommand, OrderDetailDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IProcessRepository _processRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public AddOrderItemCommandHandler(
        IOrderRepository orderRepository,
        IProductCategoryRepository categoryRepository,
        IProcessRepository processRepository,
        IOrdersUnitOfWork unitOfWork)
    {
        _orderRepository = orderRepository;
        _categoryRepository = categoryRepository;
        _processRepository = processRepository;
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderDetailDto> Handle(AddOrderItemCommand request, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdWithItemsAsync(request.OrderId, cancellationToken)
            ?? throw new NotFoundException("Order", request.OrderId);

        var category = await _categoryRepository.GetByIdWithDetailsAsync(request.ProductCategoryId, cancellationToken)
            ?? throw new NotFoundException("ProductCategory", request.ProductCategoryId);

        var item = order.AddItem(request.ProductCategoryId, request.ProductName, request.Quantity, request.Notes);

        // When the order has manual processes, those override the category list
        // (mirrors CreateOrderCommandHandler). Otherwise fall back to category.
        IEnumerable<(Guid ProcessId, ComplexityType? Complexity)> processSource = order.HasManualProcesses
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
                foreach (var sub in process.SubProcesses.OrderBy(s => s.SequenceOrder))
                {
                    oip.AddSubProcess(sub.Id);
                }
            }
        }

        // Explicitly add the new item to the DbContext — EF Core's DetectChanges
        // does not reliably discover new entities added to a tracked entity's
        // navigation collection when using PropertyAccessMode.Field.
        _orderRepository.AddItem(item);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        var refreshed = await _orderRepository.GetByIdWithFullDetailsAsync(request.OrderId, cancellationToken);
        return refreshed!.Adapt<OrderDetailDto>();
    }
}
