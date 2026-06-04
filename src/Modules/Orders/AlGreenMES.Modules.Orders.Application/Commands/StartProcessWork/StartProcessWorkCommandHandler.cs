using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.DTOs.Events;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Production.Domain.Repositories;
using Mapster;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AlGreenMES.Modules.Orders.Application.Commands.StartProcessWork;

public class StartProcessWorkCommandHandler : IRequestHandler<StartProcessWorkCommand, OrderItemProcessDto>
{
    private readonly IOrderItemProcessRepository _processRepository;
    private readonly IProcessRepository _productionProcessRepository;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IOrdersUnitOfWork _unitOfWork;
    private readonly IProductionEventService _eventService;
    private readonly ILogger<StartProcessWorkCommandHandler> _logger;

    public StartProcessWorkCommandHandler(
        IOrderItemProcessRepository processRepository,
        IProcessRepository productionProcessRepository,
        IProductCategoryRepository categoryRepository,
        IOrdersUnitOfWork unitOfWork,
        IProductionEventService eventService,
        ILogger<StartProcessWorkCommandHandler> logger)
    {
        _processRepository = processRepository;
        _productionProcessRepository = productionProcessRepository;
        _categoryRepository = categoryRepository;
        _unitOfWork = unitOfWork;
        _eventService = eventService;
        _logger = logger;
    }

    public async Task<OrderItemProcessDto> Handle(StartProcessWorkCommand request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[StartProcess] Starting process {ProcessId} for user {UserId}",
            request.OrderItemProcessId, request.UserId);

        var process = await _processRepository.GetByIdWithFullDetailsAsync(request.OrderItemProcessId, cancellationToken);
        if (process == null)
            throw new NotFoundException("OrderItemProcess", request.OrderItemProcessId);

        _logger.LogInformation("[StartProcess] Found process. Status={Status}, OrderStatus={OrderStatus}, SubProcessCount={SubCount}",
            process.Status, process.OrderItem.Order.Status, process.SubProcesses.Count);

        if (process.OrderItem.Order.Status != OrderStatus.Active)
            throw new DomainException("ORDER_NOT_ACTIVE", "Order must be active to start work.");

        // Validate dependencies via ProductCategoryDependency
        var orderItem = process.OrderItem;
        var category = await _categoryRepository.GetByIdWithDetailsAsync(orderItem.ProductCategoryId, cancellationToken);
        if (category != null)
        {
            var dependencies = category.Dependencies
                .Where(d => d.ProcessId == process.ProcessId)
                .Select(d => d.DependsOnProcessId)
                .ToList();

            foreach (var depProcessId in dependencies)
            {
                var depProcess = orderItem.Processes.FirstOrDefault(p => p.ProcessId == depProcessId);
                if (depProcess != null && depProcess.Status != ProcessStatus.Completed && depProcess.Status != ProcessStatus.Withdrawn)
                {
                    var depInfo = await _productionProcessRepository.GetByIdAsync(depProcessId, cancellationToken);
                    var depName = depInfo?.Name ?? depProcessId.ToString();
                    throw new DomainException("DEPENDENCY_NOT_MET",
                        $"Cannot start. Process '{depName}' must be completed first.");
                }
            }
        }

        process.Start(request.UserId);
        _logger.LogInformation("[StartProcess] Process started. Starting first sub-process...");

        // Load production process to get SubProcess SequenceOrder for correct ordering
        var productionProcess = await _productionProcessRepository.GetByIdWithSubProcessesAsync(process.ProcessId, cancellationToken);
        var subProcessOrder = productionProcess?.SubProcesses
            .ToDictionary(sp => sp.Id, sp => sp.SequenceOrder) ?? new();

        var firstSubProcess = process.SubProcesses
            .Where(sp => !sp.IsWithdrawn)
            .OrderBy(sp => subProcessOrder.GetValueOrDefault(sp.SubProcessId, 0))
            .FirstOrDefault();

        if (firstSubProcess != null)
        {
            firstSubProcess.Start();
            firstSubProcess.StartLog(request.UserId);
            _logger.LogInformation("[StartProcess] SubProcess {SubId} started with log for user {UserId}",
                firstSubProcess.Id, request.UserId);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("[StartProcess] Changes saved. Sending event...");

        await _eventService.NotifyProcessStartedAsync(
            new ProcessStartedEvent(
                process.Id,
                process.ProcessId,
                process.OrderItem.Order.Id,
                process.OrderItem.Order.OrderNumber,
                process.TenantId), cancellationToken);

        _logger.LogInformation("[StartProcess] Event sent. Mapping DTO...");
        var result = process.Adapt<OrderItemProcessDto>();
        _logger.LogInformation("[StartProcess] Done. Returning DTO with {SubCount} sub-processes",
            result.SubProcesses.Count);
        return result;
    }
}
