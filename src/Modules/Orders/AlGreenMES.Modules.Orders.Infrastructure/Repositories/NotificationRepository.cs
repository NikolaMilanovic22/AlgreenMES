using AlGreenMES.BuildingBlocks.Common.Pagination;
using AlGreenMES.Modules.Orders.Domain.Entities;
using AlGreenMES.Modules.Orders.Domain.Repositories;
using AlGreenMES.Modules.Orders.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AlGreenMES.Modules.Orders.Infrastructure.Repositories;

public class NotificationRepository : INotificationRepository
{
    private readonly OrdersDbContext _dbContext;

    public NotificationRepository(OrdersDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Notification?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
    }

    public async Task<IReadOnlyList<Notification>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetUnreadCountAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Bell badge caps display at "99+" via antd <Badge> defaults, so counting
        // beyond 100 is wasted work — and grows linearly per user as notifications
        // accumulate (Sentry weekly report 27.06.2026 flagged unread-count latency
        // climbing 82→259ms as the alblue staging tenant hit 300+ unread per
        // manager/coord with virtually no one clicking the bell). LIMIT 100
        // keeps the response constant-time regardless of how many unread sit
        // in the table.
        return await _dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .Take(100)
            .CountAsync(cancellationToken);
    }

    public async Task AddAsync(Notification notification, CancellationToken cancellationToken = default)
    {
        await _dbContext.Notifications.AddAsync(notification, cancellationToken);
    }

    public void Delete(Notification notification)
    {
        _dbContext.Notifications.Remove(notification);
    }

    public async Task DeleteAllByUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _dbContext.Notifications
            .Where(n => n.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
    }

    public async Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        await _dbContext.Notifications
            .Where(n => n.UserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(n => n.SetProperty(x => x.IsRead, true), cancellationToken);
    }

    public async Task<PagedResult<Notification>> GetPagedAsync(Guid userId, bool? isRead, string? search, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Notifications.Where(n => n.UserId == userId);

        if (isRead.HasValue)
            query = query.Where(n => n.IsRead == isRead.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(n => n.Message.ToLower().Contains(search.ToLower()));

        query = query.OrderByDescending(n => n.CreatedAt);

        return await query.ToPagedResultAsync(page, pageSize, cancellationToken);
    }
}
