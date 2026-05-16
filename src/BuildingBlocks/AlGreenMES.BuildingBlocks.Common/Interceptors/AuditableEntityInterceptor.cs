using AlGreenMES.BuildingBlocks.Common.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AlGreenMES.BuildingBlocks.Common.Interceptors;

/// <summary>
/// Auto-populates CreatedAt / CreatedByUserId / UpdatedAt / UpdatedByUserId on
/// entities implementing IAuditableEntity at SaveChanges time. Defense in depth:
/// handlers don't need to remember to call SetUpdated() — the interceptor runs
/// at the DbContext layer for every save.
/// </summary>
public class AuditableEntityInterceptor : SaveChangesInterceptor
{
    private readonly ICurrentUserService _currentUserService;

    public AuditableEntityInterceptor(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        UpdateEntities(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void UpdateEntities(DbContext? context)
    {
        if (context == null) return;

        Guid? userId = null;
        if (_currentUserService.IsAuthenticated)
        {
            try { userId = _currentUserService.GetCurrentUserId(); }
            catch { /* background/system context without HTTP user */ }
        }

        var utcNow = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries<IAuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Property(nameof(IAuditableEntity.CreatedAt)).CurrentValue = utcNow;
                entry.Property(nameof(IAuditableEntity.UpdatedAt)).CurrentValue = utcNow;
                if (userId.HasValue)
                {
                    entry.Property(nameof(IAuditableEntity.CreatedByUserId)).CurrentValue = userId;
                    entry.Property(nameof(IAuditableEntity.UpdatedByUserId)).CurrentValue = userId;
                }
            }
            else if (entry.State == EntityState.Modified)
            {
                // Don't overwrite Created* on updates.
                entry.Property(nameof(IAuditableEntity.CreatedAt)).IsModified = false;
                entry.Property(nameof(IAuditableEntity.CreatedByUserId)).IsModified = false;

                entry.Property(nameof(IAuditableEntity.UpdatedAt)).CurrentValue = utcNow;
                if (userId.HasValue)
                {
                    entry.Property(nameof(IAuditableEntity.UpdatedByUserId)).CurrentValue = userId;
                }
            }
        }
    }
}
