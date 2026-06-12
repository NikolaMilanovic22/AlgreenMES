using AlGreenMES.Modules.Identity.Domain.Entities;

namespace AlGreenMES.Modules.Identity.Domain.Repositories;

public interface IUserRoleChangeLogRepository
{
    Task AddAsync(UserRoleChangeLog log, CancellationToken cancellationToken = default);
}
