using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using AlGreenMES.Modules.Orders.Api.Requests;
using AlGreenMES.Modules.Orders.Application.Commands.ActivateOrder;
using AlGreenMES.Modules.Orders.Application.Commands.AddOrderItem;
using AlGreenMES.Modules.Orders.Application.Commands.AddSpecialRequest;
using AlGreenMES.Modules.Orders.Application.Commands.CancelOrder;
using AlGreenMES.Modules.Orders.Application.Commands.ChangePriority;
using AlGreenMES.Modules.Orders.Application.Commands.SetOrderInvoiced;
using AlGreenMES.Modules.Orders.Application.Commands.CreateOrder;
using AlGreenMES.Modules.Orders.Application.Commands.DeleteOrderAttachment;
using AlGreenMES.Modules.Orders.Application.Commands.OverrideComplexity;
using AlGreenMES.Modules.Orders.Application.Commands.PauseOrder;
using AlGreenMES.Modules.Orders.Application.Commands.RemoveOrderItem;
using AlGreenMES.Modules.Orders.Application.Commands.RemoveSpecialRequest;
using AlGreenMES.Modules.Orders.Application.Commands.ReopenOrder;
using AlGreenMES.Modules.Orders.Application.Commands.ResumeOrder;
using AlGreenMES.Modules.Orders.Application.Commands.UpdateOrder;
using AlGreenMES.Modules.Orders.Application.Commands.UploadOrderAttachment;
using AlGreenMES.Modules.Orders.Application.Commands.WithdrawOrderToProcess;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Application.Queries.GetOrderAttachments;
using AlGreenMES.Modules.Orders.Application.Queries.GetOrderById;
using AlGreenMES.Modules.Orders.Application.Queries.GetOrders;
using AlGreenMES.Modules.Orders.Application.Queries.GetOrdersMasterView;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace AlGreenMES.Modules.Orders.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IOrderAttachmentRepository _attachmentRepository;
    private readonly IFileStorageService _fileStorageService;
    private readonly ITenantService _tenantService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(
        IMediator mediator,
        IOrderAttachmentRepository attachmentRepository,
        IFileStorageService fileStorageService,
        ITenantService tenantService,
        ILogger<OrdersController> logger)
    {
        _mediator = mediator;
        _attachmentRepository = attachmentRepository;
        _fileStorageService = fileStorageService;
        _tenantService = tenantService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders(
        [FromQuery] OrderStatus? status,
        [FromQuery] OrderType? orderType,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetOrdersQuery
        {
            TenantId = _tenantService.GetCurrentTenantId(),
            Status = status,
            OrderType = orderType,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Page = page,
            PageSize = pageSize,
            Search = search
        }, cancellationToken);
        return Ok(result);
    }

    [HttpGet("master-view")]
    public async Task<IActionResult> GetOrdersMasterView(
        [FromQuery] OrderStatus? status,
        [FromQuery] OrderType? orderType,
        [FromQuery] bool? isInvoiced,
        [FromQuery] DateTime? dateFrom,
        [FromQuery] DateTime? dateTo,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDirection = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetOrdersMasterViewQuery
        {
            TenantId = _tenantService.GetCurrentTenantId(),
            Status = status,
            OrderType = orderType,
            IsInvoiced = isInvoiced,
            DateFrom = dateFrom,
            DateTo = dateTo,
            Page = page,
            PageSize = pageSize,
            Search = search,
            SortBy = sortBy,
            SortDirection = sortDirection,
        }, cancellationToken);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetOrderById(Guid id, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrderByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<IActionResult> CreateOrder([FromForm] CreateOrderRequest request, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID claim not found in token."));

        // FE sends ComplexityType as a string ("S"/"L"/"T"); System.Text.Json defaults
        // to numeric enums, so without the converter deserialization throws and we'd
        // silently lose the whole manual-processes list.
        var jsonOpts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        jsonOpts.Converters.Add(new JsonStringEnumConverter());
        var manualProcesses = ParseJsonList<CreateOrderManualProcessInput>(request.ManualProcessesJson, jsonOpts);
        var manualDependencies = ParseJsonList<CreateOrderManualDependencyInput>(request.ManualDependenciesJson, jsonOpts);

        var result = await _mediator.Send(
            new CreateOrderCommand(
                _tenantService.GetCurrentTenantId(), request.OrderNumber, request.DeliveryDate, request.Priority, request.OrderType, userId, request.Notes, request.CustomWarningDays, request.CustomCriticalDays,
                request.Items?.Select(i => new Application.Commands.CreateOrder.CreateOrderItemInput(i.ProductCategoryId, i.ProductName, i.Quantity, i.Notes,
                    i.Attachments?.Select(f => new Application.Commands.CreateOrder.CreateOrderAttachmentInput(f.FileName, f.ContentType, f.Length, f.OpenReadStream())).ToList())).ToList(),
                request.Attachments?.Select(f => new Application.Commands.CreateOrder.CreateOrderAttachmentInput(f.FileName, f.ContentType, f.Length, f.OpenReadStream())).ToList(),
                manualProcesses, manualDependencies),
            cancellationToken);
        return CreatedAtAction(nameof(GetOrderById), new { id = result.Id }, result);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager")]
    public async Task<IActionResult> UpdateOrder(Guid id, [FromBody] UpdateOrderRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new UpdateOrderCommand(
                id, request.OrderNumber, request.DeliveryDate, request.Notes, request.CustomWarningDays, request.CustomCriticalDays,
                request.AddItems?.Select(i => new Application.Commands.UpdateOrder.UpdateOrderItemInput(i.ProductCategoryId, i.ProductName, i.Quantity, i.Notes)).ToList(),
                request.RemoveItemIds,
                request.ComplexityOverrides?.Select(c => new Application.Commands.UpdateOrder.UpdateOrderComplexityInput(c.ItemId, c.ProcessId, c.Complexity)).ToList(),
                request.AddSpecialRequests?.Select(s => new Application.Commands.UpdateOrder.UpdateOrderSpecialRequestAdd(s.ItemId, s.SpecialRequestTypeId)).ToList(),
                request.RemoveSpecialRequests?.Select(s => new Application.Commands.UpdateOrder.UpdateOrderSpecialRequestRemove(s.ItemId, s.SpecialRequestId)).ToList()),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/activate")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> ActivateOrder(Guid id, [FromBody] ActivateOrderRequest? request = null, CancellationToken cancellationToken = default)
    {
        await _mediator.Send(new ActivateOrderCommand(id, request?.ResetProcessIds), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/pause")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> PauseOrder(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new PauseOrderCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/resume")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> ResumeOrder(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ResumeOrderCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> CancelOrder(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelOrderCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:guid}/reopen")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager")]
    public async Task<IActionResult> ReopenOrder(Guid id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new ReopenOrderCommand(id), cancellationToken);
        return NoContent();
    }

    [HttpPost("{orderId:guid}/items")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager")]
    public async Task<IActionResult> AddOrderItem(Guid orderId, [FromBody] AddOrderItemRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AddOrderItemCommand(orderId, request.ProductCategoryId, request.ProductName, request.Quantity, request.Notes),
            cancellationToken);
        return Ok(result);
    }

    [HttpDelete("{orderId:guid}/items/{itemId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager")]
    public async Task<IActionResult> RemoveOrderItem(Guid orderId, Guid itemId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RemoveOrderItemCommand(orderId, itemId), cancellationToken);
        return NoContent();
    }

    [HttpPut("{id:guid}/priority")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> ChangePriority(Guid id, [FromBody] ChangePriorityRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new ChangePriorityCommand(id, request.Priority), cancellationToken);
        return Ok(result);
    }

    [HttpPut("{id:guid}/invoiced")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager,Coordinator")]
    public async Task<IActionResult> SetInvoiced(Guid id, [FromBody] SetOrderInvoicedRequest request, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new SetOrderInvoicedCommand(id, request.IsInvoiced), cancellationToken);
        return Ok(result);
    }

    [HttpPost("{id:guid}/withdraw")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,Coordinator")]
    public async Task<IActionResult> WithdrawOrderToProcess(Guid id, [FromBody] WithdrawOrderToProcessRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new WithdrawOrderToProcessCommand(id, request.TargetProcessId, request.Reason, request.UserId), cancellationToken);
        return NoContent();
    }

    [HttpPost("{orderId:guid}/items/{itemId:guid}/special-requests")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager")]
    public async Task<IActionResult> AddSpecialRequest(Guid orderId, Guid itemId, [FromBody] AddSpecialRequestRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new AddSpecialRequestCommand(orderId, itemId, request.SpecialRequestTypeId), cancellationToken);
        return NoContent();
    }

    [HttpDelete("{orderId:guid}/items/{itemId:guid}/special-requests/{specialRequestId:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager")]
    public async Task<IActionResult> RemoveSpecialRequest(Guid orderId, Guid itemId, Guid specialRequestId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new RemoveSpecialRequestCommand(orderId, itemId, specialRequestId), cancellationToken);
        return NoContent();
    }

    [HttpPut("{orderId:guid}/items/{itemId:guid}/processes/{processId:guid}/complexity")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager")]
    public async Task<IActionResult> OverrideComplexity(Guid orderId, Guid itemId, Guid processId, [FromBody] OverrideComplexityRequest request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new OverrideComplexityCommand(orderId, itemId, processId, request.Complexity), cancellationToken);
        return NoContent();
    }

    // --- Attachments ---

    [HttpPost("{orderId:guid}/attachments")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<IActionResult> UploadAttachment(Guid orderId, IFormFile file, [FromQuery] Guid? orderItemId, CancellationToken cancellationToken)
    {
        var userId = Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
            ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new UnauthorizedAccessException("User ID claim not found in token."));

        var result = await _mediator.Send(new UploadOrderAttachmentCommand(
            orderId, _tenantService.GetCurrentTenantId(), userId,
            file.FileName, file.ContentType, file.Length, file.OpenReadStream(),
            orderItemId),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("{orderId:guid}/attachments")]
    public async Task<IActionResult> GetAttachments(Guid orderId, [FromQuery] Guid? orderItemId, CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOrderAttachmentsQuery(orderId, orderItemId), cancellationToken);
        return Ok(result);
    }

    [HttpGet("{orderId:guid}/attachments/{id:guid}/download")]
    public async Task<IActionResult> DownloadAttachment(Guid orderId, Guid id, CancellationToken cancellationToken)
    {
        var attachment = await _attachmentRepository.GetByIdAsync(id, cancellationToken);
        if (attachment == null || attachment.OrderId != orderId)
            return NotFound();

        var currentTenantId = _tenantService.GetCurrentTenantId();
        if (attachment.TenantId != currentTenantId)
        {
            _logger.LogWarning(
                "Cross-tenant attachment access attempt: user tenant {UserTenantId} requested attachment {AttachmentId} from tenant {AttachmentTenantId}",
                currentTenantId, id, attachment.TenantId);
            return NotFound();
        }

        var stream = await _fileStorageService.GetFileAsync(attachment.StoragePath, cancellationToken);
        if (stream == null)
            return NotFound();

        // For PDFs, serve inline so browsers/apps can display them directly
        if (attachment.ContentType == "application/pdf")
        {
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{attachment.OriginalFileName}\"";
            return File(stream, attachment.ContentType);
        }
        return File(stream, attachment.ContentType, attachment.OriginalFileName);
    }

    [HttpDelete("{orderId:guid}/attachments/{id:guid}")]
    [Authorize(Roles = "SuperAdmin,Admin,Manager,SalesManager")]
    public async Task<IActionResult> DeleteAttachment(Guid orderId, Guid id, [FromQuery] Guid tenantId, CancellationToken cancellationToken)
    {
        await _mediator.Send(new DeleteOrderAttachmentCommand(orderId, id, tenantId), cancellationToken);
        return NoContent();
    }

    private static List<T>? ParseJsonList<T>(string? json, JsonSerializerOptions opts)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        // Surface parse failures as 400s — silently dropping the list hides real bugs
        // (we lost a half-day to that with the ComplexityType enum converter).
        return JsonSerializer.Deserialize<List<T>>(json, opts);
    }
}
