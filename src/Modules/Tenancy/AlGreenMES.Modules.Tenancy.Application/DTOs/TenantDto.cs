namespace AlGreenMES.Modules.Tenancy.Application.DTOs;

public record TenantDto(
    Guid Id,
    string Name,
    string Code,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string? LogoUrl = null);
