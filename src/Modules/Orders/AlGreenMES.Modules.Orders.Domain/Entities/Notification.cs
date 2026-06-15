using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.Modules.Orders.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

public class Notification : TenantEntity
{
    public Guid UserId { get; private set; }
    public NotificationType Type { get; private set; }
    public string Title { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public string? ReferenceType { get; private set; }
    public Guid? ReferenceId { get; private set; }
    /// <summary>
    /// Optional JSON payload with structured params (e.g. {"materialCode":"100",
    /// "materialName":"Profil AL"}). FE prefers rendering via i18n templates
    /// keyed on <see cref="Type"/> using these params; falls back to Title /
    /// Message when missing.
    /// </summary>
    public string? ParamsJson { get; private set; }
    public bool IsRead { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    private Notification()
    {
    }

    public static Notification Create(Guid tenantId, Guid userId, NotificationType type,
        string title, string message, string? referenceType = null, Guid? referenceId = null,
        string? paramsJson = null)
    {
        return new Notification
        {
            TenantId = tenantId,
            UserId = userId,
            Type = type,
            Title = title,
            Message = message,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ParamsJson = paramsJson,
            IsRead = false
        };
    }

    public void MarkRead()
    {
        IsRead = true;
    }

    public void MarkUnread()
    {
        IsRead = false;
    }
}
