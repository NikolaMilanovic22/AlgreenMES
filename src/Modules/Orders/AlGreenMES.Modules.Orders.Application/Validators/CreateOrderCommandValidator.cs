using AlGreenMES.Modules.Orders.Application.Commands.CreateOrder;
using FluentValidation;

namespace AlGreenMES.Modules.Orders.Application.Validators;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.OrderNumber).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DeliveryDate).GreaterThan(DateTime.UtcNow.Date);
        RuleFor(x => x.Priority).GreaterThan(0);
        // OrderType used to be a 4-value C# enum (IsInEnum); after 20.06.2026
        // it's a free-form string referencing OrderType.Code in the per-tenant
        // OrderTypes table. The handler validates code-exists-for-tenant;
        // here we just enforce non-empty + length.
        RuleFor(x => x.OrderType).NotEmpty().MaximumLength(20);
        RuleFor(x => x.CreatedByUserId).NotEmpty();
    }
}
