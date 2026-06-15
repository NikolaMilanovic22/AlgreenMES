using AlGreenMES.Modules.Identity.Application.Commands.ResetPassword;
using FluentValidation;

namespace AlGreenMES.Modules.Identity.Application.Validators;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        PasswordRule.Apply(RuleFor(x => x.NewPassword));
    }
}
