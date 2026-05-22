using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

public class OrderItemSubProcess : TenantEntity
{
    public Guid OrderItemProcessId { get; private set; }
    public Guid SubProcessId { get; private set; }
    public SubProcessStatus Status { get; private set; }
    public int TotalDurationMinutes { get; private set; }
    public bool IsWithdrawn { get; private set; }
    public DateTime? WithdrawnAt { get; private set; }
    public Guid? WithdrawnByUserId { get; private set; }
    public string? WithdrawnReason { get; private set; }
    public Guid? StoppedByUserId { get; private set; }
    public string? StoppedReason { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>
    /// When set, this sub-process was paused by a tablet-station logout
    /// (its open log was closed in PauseStation). Next ResumeStation call
    /// should auto-resume it (StartLog). Null means manually paused or
    /// never running — must not auto-resume.
    /// </summary>
    public DateTime? PausedByStationAt { get; private set; }

    public OrderItemProcess OrderItemProcess { get; private set; } = null!;

    private readonly List<OrderItemSubProcessLog> _logs = new();
    public IReadOnlyCollection<OrderItemSubProcessLog> Logs => _logs.AsReadOnly();

    private OrderItemSubProcess()
    {
    }

    internal static OrderItemSubProcess Create(Guid tenantId, Guid orderItemProcessId, Guid subProcessId)
    {
        return new OrderItemSubProcess
        {
            TenantId = tenantId,
            OrderItemProcessId = orderItemProcessId,
            SubProcessId = subProcessId,
            Status = SubProcessStatus.Pending
        };
    }

    public void Start()
    {
        if (Status != SubProcessStatus.Pending)
            throw new DomainException("INVALID_STATUS", "Can only start pending sub-processes.");
        Status = SubProcessStatus.InProgress;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Complete()
    {
        Status = SubProcessStatus.Completed;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Stop(Guid userId, string reason)
    {
        Status = SubProcessStatus.Stopped;
        StoppedByUserId = userId;
        StoppedReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Withdraw(Guid userId, string reason)
    {
        IsWithdrawn = true;
        WithdrawnAt = DateTime.UtcNow;
        WithdrawnByUserId = userId;
        WithdrawnReason = reason;
        Status = SubProcessStatus.Withdrawn;
        UpdatedAt = DateTime.UtcNow;
    }

    public void AddDuration(int minutes)
    {
        TotalDurationMinutes += minutes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ResetDuration()
    {
        TotalDurationMinutes = 0;
        Status = SubProcessStatus.Pending;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ReturnToPending()
    {
        Status = SubProcessStatus.Pending;
        UpdatedAt = DateTime.UtcNow;
    }

    public OrderItemSubProcessLog StartLog(Guid userId)
    {
        var openLog = _logs.FirstOrDefault(l => l.EndTime == null);
        if (openLog != null)
            throw new DomainException("LOG_ALREADY_OPEN", "There is already an open log entry for this sub-process.");

        var log = OrderItemSubProcessLog.Start(TenantId, Id, userId);
        _logs.Add(log);
        PausedByStationAt = null; // started — no longer eligible for auto-resume
        UpdatedAt = DateTime.UtcNow;
        return log;
    }

    /// <summary>
    /// Mark this sub-process as paused by a tablet-station logout so the
    /// next ResumeStation call can auto-restart its log.
    /// </summary>
    public void PauseByStation()
    {
        PausedByStationAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public OrderItemSubProcessLog? GetOpenLog()
    {
        return _logs.FirstOrDefault(l => l.EndTime == null);
    }
}
