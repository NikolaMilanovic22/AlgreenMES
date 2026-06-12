using AlGreenMES.Modules.Identity.Application.Commands.ChangePassword;
using FluentValidation;

namespace AlGreenMES.Modules.Identity.Application.Validators;

public class ChangePasswordCommandValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CurrentPassword).NotEmpty();
        PasswordRule.Apply(RuleFor(x => x.NewPassword));
    }
}
