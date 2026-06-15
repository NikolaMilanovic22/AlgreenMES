using FluentValidation;

namespace AlGreenMES.Modules.Identity.Application.Validators;

/// <summary>
/// Shared password policy applied to every place a new password lands in
/// the system — Login isn't here because login validates against the
/// already-stored hash, not the rule. Three commands write a new hash:
/// CreateUser (admin sets initial password), ChangePassword (user changes
/// own), ResetPassword (admin overrides). All three call <see cref="Apply"/>
/// so bumping the policy is a one-line change here.
///
/// Current rule (12.06.2026 bump from MinimumLength 6 → 8 + composition):
/// - at least 8 characters
/// - at most 100 characters (defense against hash-collision DoS)
/// - at least one letter (so digit-only passwords like "12345678" fail)
/// - at least one digit (so word-only passwords like "passwords" fail)
///
/// The FE form validation in apps/dashboard/src/utils/password.ts mirrors
/// this exactly. Keep them in lockstep — drift causes the FE to accept a
/// password the BE then rejects with a 400, which is bad UX.
/// </summary>
public static class PasswordRule
{
    public static IRuleBuilderOptions<T, string> Apply<T>(IRuleBuilder<T, string> rule)
    {
        return rule
            .NotEmpty().WithMessage("Password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .MaximumLength(100).WithMessage("Password must be at most 100 characters.")
            .Matches("[A-Za-z]").WithMessage("Password must contain at least one letter.")
            .Matches("[0-9]").WithMessage("Password must contain at least one digit.");
    }
}
