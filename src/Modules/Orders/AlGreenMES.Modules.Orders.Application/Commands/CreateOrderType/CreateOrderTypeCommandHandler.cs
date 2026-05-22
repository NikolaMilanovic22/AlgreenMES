using System.Text.RegularExpressions;
using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Application.DTOs;
using AlGreenMES.Modules.Orders.Application.Interfaces;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using Mapster;
using MediatR;
using OrderTypeEntity = AlGreenMES.Modules.Orders.Domain.Entities.OrderTypes.OrderType;

namespace AlGreenMES.Modules.Orders.Application.Commands.CreateOrderType;

public class CreateOrderTypeCommandHandler : IRequestHandler<CreateOrderTypeCommand, OrderTypeDto>
{
    private readonly IOrderTypeRepository _repository;
    private readonly IOrdersUnitOfWork _unitOfWork;

    public CreateOrderTypeCommandHandler(IOrderTypeRepository repository, IOrdersUnitOfWork unitOfWork)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
    }

    public async Task<OrderTypeDto> Handle(CreateOrderTypeCommand request, CancellationToken cancellationToken)
    {
        // Code is an internal identifier (bridge to the legacy OrderType enum on
        // Order). Admins shouldn't have to type it; if blank, derive from Name and
        // append a numeric suffix until unique within the tenant.
        var code = string.IsNullOrWhiteSpace(request.Code)
            ? await GenerateUniqueCodeAsync(request.Name, request.TenantId, cancellationToken)
            : request.Code;

        if (await _repository.ExistsByCodeAsync(code, request.TenantId, cancellationToken))
            throw new DomainException("ORDER_TYPE_CODE_EXISTS", $"An order type with code '{code}' already exists.");

        var orderType = OrderTypeEntity.Create(request.TenantId, code, request.Name, request.AllowsManualProcesses);
        await _repository.AddAsync(orderType, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return orderType.Adapt<OrderTypeDto>();
    }

    private async Task<string> GenerateUniqueCodeAsync(string name, Guid tenantId, CancellationToken ct)
    {
        var slug = Regex.Replace(name.ToUpperInvariant(), "[^A-Z0-9]+", "_").Trim('_');
        if (string.IsNullOrEmpty(slug)) slug = "TYPE";
        if (slug.Length > 40) slug = slug.Substring(0, 40);

        var candidate = slug;
        var n = 1;
        while (await _repository.ExistsByCodeAsync(candidate, tenantId, ct))
        {
            n++;
            candidate = $"{slug}_{n}";
        }
        return candidate;
    }
}
