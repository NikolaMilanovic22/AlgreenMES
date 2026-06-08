using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlGreenMES.Modules.Identity.Domain.Entities;

public class User : AuditableEntity
{
    public string Email { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public string FirstName { get; private set; } = null!;
    public string LastName { get; private set; } = null!;
    public UserRole Role { get; private set; }
    public bool CanIncludeWithdrawnInAnalysis { get; private set; }
    public bool IsActive { get; private set; }

    private readonly List<UserProcess> _userProcesses = new();
    public IReadOnlyCollection<UserProcess> UserProcesses => _userProcesses.AsReadOnly();

    private readonly List<UserRoleAssignment> _additionalRoles = new();
    public IReadOnlyCollection<UserRoleAssignment> AdditionalRoles => _additionalRoles.AsReadOnly();

    public string FullName => $"{FirstName} {LastName}";

    /// <summary>
    /// Effective role set = primary <see cref="Role"/> ∪ extras from
    /// <see cref="AdditionalRoles"/>. Used by JWT generation (one Role
    /// claim per effective role) and by <see cref="HasRole"/>.
    /// </summary>
    public IReadOnlySet<UserRole> EffectiveRoles
    {
        get
        {
            var set = new HashSet<UserRole> { Role };
            foreach (var r in _additionalRoles) set.Add(r.Role);
            return set;
        }
    }

    public bool HasRole(UserRole role) =>
        Role == role || _additionalRoles.Any(r => r.Role == role);

    private User()
    {
    }

    public static User Create(Guid tenantId, string email, string passwordHash, string firstName, string lastName, UserRole role)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("USER_EMAIL_REQUIRED", "User email is required.");

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("USER_PASSWORD_REQUIRED", "User password is required.");

        if (string.IsNullOrWhiteSpace(firstName))
            throw new DomainException("USER_FIRST_NAME_REQUIRED", "User first name is required.");

        if (string.IsNullOrWhiteSpace(lastName))
            throw new DomainException("USER_LAST_NAME_REQUIRED", "User last name is required.");

        var user = new User
        {
            TenantId = tenantId,
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            FirstName = firstName.Trim(),
            LastName = lastName.Trim(),
            Role = role,
            IsActive = true
        };

        return user;
    }

    public void Update(string firstName, string lastName, UserRole role, bool isActive, bool canIncludeWithdrawnInAnalysis = false)
    {
        if (string.IsNullOrWhiteSpace(firstName))
            throw new DomainException("USER_FIRST_NAME_REQUIRED", "User first name is required.");

        if (string.IsNullOrWhiteSpace(lastName))
            throw new DomainException("USER_LAST_NAME_REQUIRED", "User last name is required.");

        FirstName = firstName.Trim();
        LastName = lastName.Trim();
        Role = role;
        IsActive = isActive;
        CanIncludeWithdrawnInAnalysis = canIncludeWithdrawnInAnalysis;
    }

    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new DomainException("USER_PASSWORD_REQUIRED", "User password is required.");

        PasswordHash = newPasswordHash;
    }

    public void AssignProcesses(Guid tenantId, IEnumerable<Guid> processIds)
    {
        _userProcesses.Clear();
        foreach (var processId in processIds.Distinct())
        {
            _userProcesses.Add(UserProcess.Create(tenantId, Id, processId));
        }
    }

    public List<Guid> GetProcessIds()
    {
        return _userProcesses.Select(up => up.ProcessId).ToList();
    }

    public bool HasProcess(Guid processId)
    {
        return _userProcesses.Any(up => up.ProcessId == processId);
    }

    /// <summary>
    /// Replaces the user's additional-role set with the given list. Primary
    /// <see cref="Role"/> is intentionally NOT included even if passed —
    /// keep that channel for the single primary-role API. Distinct + the
    /// primary-role exclusion guarantees no duplicate role claims at JWT
    /// emission time.
    /// </summary>
    public void AssignAdditionalRoles(Guid tenantId, IEnumerable<UserRole> roles)
    {
        _additionalRoles.Clear();
        foreach (var role in roles.Distinct().Where(r => r != Role))
        {
            _additionalRoles.Add(UserRoleAssignment.Create(tenantId, Id, role));
        }
    }
}
