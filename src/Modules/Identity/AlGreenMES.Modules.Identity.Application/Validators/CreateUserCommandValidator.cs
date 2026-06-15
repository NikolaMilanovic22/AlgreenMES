using AlGreenMES.Modules.Identity.Application.Commands.CreateUser;
using AlGreenMES.Modules.Identity.Domain.Entities;
using FluentValidation;

namespace AlGreenMES.Modules.Identity.Application.Validators;

public class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        PasswordRule.Apply(RuleFor(x => x.Password));
        RuleFor(x => x.FirstName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.LastName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Role).IsInEnum();
        RuleFor(x => x.ProcessIds).NotEmpty().When(x => x.Role == UserRole.Department);
    }
}
