using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Orders.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Orders.Domain.Entities;

public class OrderItemProcess : TenantEntity
{
    public Guid OrderItemId { get; private set; }
    public Guid ProcessId { get; private set; }
    public ComplexityType? Complexity { get; private set; }
    public bool ComplexityOverridden { get; private set; }
    public ProcessStatus Status { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public int TotalDurationMinutes { get; private set; }

    public bool IsWithdrawn { get; private set; }
    public DateTime? WithdrawnAt { get; private set; }
    public Guid? WithdrawnByUserId { get; private set; }
    public string? WithdrawnReason { get; private set; }

    public DateTime? BlockedAt { get; private set; }
    public Guid? BlockedByUserId { get; private set; }
    public string? BlockReason { get; private set; }
    public DateTime? UnblockedAt { get; private set; }
    public Guid? UnblockedByUserId { get; private set; }

    public DateTime? StoppedAt { get; private set; }
    public Guid? StoppedByUserId { get; private set; }
    public string? StoppedReason { get; private set; }

    public DateTime? PausedAt { get; private set; }
    public DateTime? ResumedAt { get; private set; }
    /// <summary>
    /// When set, this process was paused by a tablet-station logout and
    /// should auto-resume on the next ResumeStation call. Null means
    /// either "not paused" or "paused manually by a worker" (which must
    /// not auto-resume).
    /// </summary>
    public DateTime? PausedByStationAt { get; private set; }

    /// <summary>
    /// Sale/Bojan can manually mark a row to be excluded from the /reports
    /// statistics + export. Excluded rows are filtered out of Vremena
    /// (process times) aggregation and from the Praćenje (time-tracking)
    /// XLSX/CSV export. They still appear in the Praćenje table itself so
    /// the user can re-include via the Uključi toggle. Persisted in DB so
    /// the choice survives sessions and is visible to all users in the
    /// tenant.
    /// </summary>
    public bool IsExcludedFromReports { get; private set; }

    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; private set; }

    public OrderItem OrderItem { get; private set; } = null!;

    private readonly List<OrderItemSubProcess> _subProcesses = new();
    public IReadOnlyCollection<OrderItemSubProcess> SubProcesses => _subProcesses.AsReadOnly();

    private OrderItemProcess()
    {
    }

    internal static OrderItemProcess Create(Guid tenantId, Guid orderItemId, Guid processId,
        ComplexityType? complexity, bool overridden)
    {
        return new OrderItemProcess
        {
            TenantId = tenantId,
            OrderItemId = orderItemId,
            ProcessId = processId,
            Complexity = complexity,
            ComplexityOverridden = overridden,
            Status = ProcessStatus.Pending
        };
    }

    public void Start()
    {
        if (Status != ProcessStatus.Pending)
            throw new DomainException("INVALID_STATUS", "Can only start pending processes.");
        Status = ProcessStatus.InProgress;
        StartedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Complete()
    {
        if (_subProcesses.Any() && !_subProcesses.All(sp => sp.Status == SubProcessStatus.Completed || sp.Status == SubProcessStatus.Withdrawn))
            throw new DomainException("SUBPROCESSES_NOT_COMPLETE", "All sub-processes must be completed first.");

        // Accumulate current session duration before completing
        if (!PausedAt.HasValue && (StartedAt.HasValue || ResumedAt.HasValue))
        {
            var sessionStart = ResumedAt ?? StartedAt ?? DateTime.UtcNow;
            var sessionSeconds = (int)(DateTime.UtcNow - sessionStart).TotalSeconds;
            TotalDurationMinutes += sessionSeconds;
        }

        Status = ProcessStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Block(Guid userId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("REASON_REQUIRED", "Block reason is required.");

        // Save running timer before blocking (for non-sub-process processes)
        if (Status == ProcessStatus.InProgress && !PausedAt.HasValue && (StartedAt.HasValue || ResumedAt.HasValue))
        {
            var sessionStart = ResumedAt ?? StartedAt ?? DateTime.UtcNow;
            var sessionSeconds = (int)(DateTime.UtcNow - sessionStart).TotalSeconds;
            TotalDurationMinutes += sessionSeconds;
        }

        Status = ProcessStatus.Blocked;
        BlockedAt = DateTime.UtcNow;
        BlockedByUserId = userId;
        BlockReason = reason;
        PausedAt = null;
        ResumedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Unblock(Guid userId, bool resetTime = false)
    {
        if (Status != ProcessStatus.Blocked)
            throw new DomainException("NOT_BLOCKED", "Process is not blocked.");

        // Return to InProgress+Paused - worker clicks Resume on tablet
        Status = ProcessStatus.InProgress;
        PausedAt = DateTime.UtcNow;
        ResumedAt = null;

        if (resetTime)
        {
            TotalDurationMinutes = 0;
            StartedAt = DateTime.UtcNow;
            CompletedAt = null;
            foreach (var sub in _subProcesses)
            {
                var openLog = sub.GetOpenLog();
                if (openLog != null) openLog.End();
                sub.ResetDuration();
            }
        }
        else
        {
            if (StartedAt == null) StartedAt = DateTime.UtcNow;
            foreach (var sub in _subProcesses)
            {
                var openLog = sub.GetOpenLog();
                if (openLog != null) openLog.End();
                sub.ReturnToPending();
            }
        }

        UnblockedAt = DateTime.UtcNow;
        UnblockedByUserId = userId;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Stop(Guid userId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("REASON_REQUIRED", "Stop reason is required.");
        Status = ProcessStatus.Stopped;
        StoppedAt = DateTime.UtcNow;
        StoppedByUserId = userId;
        StoppedReason = reason;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Withdraw(Guid userId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("REASON_REQUIRED", "Withdrawal reason is required.");
        IsWithdrawn = true;
        WithdrawnAt = DateTime.UtcNow;
        WithdrawnByUserId = userId;
        WithdrawnReason = reason;
        Status = ProcessStatus.Withdrawn;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Pause()
    {
        if (PausedAt.HasValue)
            throw new DomainException("ALREADY_PAUSED", "Process is already paused.");

        // Accumulate current session duration in seconds
        var sessionStart = ResumedAt ?? StartedAt ?? DateTime.UtcNow;
        var sessionSeconds = (int)(DateTime.UtcNow - sessionStart).TotalSeconds;
        TotalDurationMinutes += sessionSeconds;

        PausedAt = DateTime.UtcNow;
        PausedByStationAt = null; // manual pause — not auto-resumable on next login
        ResumedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Pause because the worker is logging out of the tablet station.
    /// Sets PausedByStationAt so the next tablet login auto-resumes this
    /// process. Skips if already paused (manually) — manual pauses must
    /// NOT auto-resume.
    /// </summary>
    public void PauseByStation()
    {
        if (PausedAt.HasValue) return;

        var sessionStart = ResumedAt ?? StartedAt ?? DateTime.UtcNow;
        var sessionSeconds = (int)(DateTime.UtcNow - sessionStart).TotalSeconds;
        TotalDurationMinutes += sessionSeconds;

        PausedAt = DateTime.UtcNow;
        PausedByStationAt = DateTime.UtcNow;
        ResumedAt = null;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ReturnToPending()
    {
        // Return to InProgress+Paused - worker clicks Resume on tablet
        Status = ProcessStatus.InProgress;
        PausedAt = DateTime.UtcNow;
        ResumedAt = null;
        if (StartedAt == null) StartedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        foreach (var sub in _subProcesses)
        {
            var openLog = sub.GetOpenLog();
            if (openLog != null) openLog.End();
            sub.ReturnToPending();
        }
    }

    public void ResetTimer()
    {
        Status = ProcessStatus.Pending;
        TotalDurationMinutes = 0;
        StartedAt = null;
        CompletedAt = null;
        ResumedAt = null;
        PausedAt = null;
        UpdatedAt = DateTime.UtcNow;

        // Close open sub-process logs and reset sub-process timers
        foreach (var sub in _subProcesses)
        {
            var openLog = sub.GetOpenLog();
            if (openLog != null)
                openLog.End();
            if (sub.Status == Enums.SubProcessStatus.InProgress)
                sub.Complete();
            sub.ResetDuration();
        }
    }

    public void ResumeTimer()
    {
        if (!PausedAt.HasValue)
            throw new DomainException("NOT_PAUSED", "Process is not paused.");

        PausedAt = null;
        PausedByStationAt = null; // resumed — no longer eligible for auto-resume
        ResumedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Restart(bool resetTime)
    {
        if (Status != ProcessStatus.Completed)
            throw new DomainException("INVALID_STATUS", "Can only restart completed processes.");

        // Return to InProgress+Paused - worker clicks Resume on tablet
        Status = ProcessStatus.InProgress;
        CompletedAt = null;
        PausedAt = DateTime.UtcNow;
        ResumedAt = null;

        if (resetTime)
        {
            TotalDurationMinutes = 0;
            StartedAt = DateTime.UtcNow;
        }
        else if (StartedAt == null)
        {
            StartedAt = DateTime.UtcNow;
        }

        // Reset sub-processes to Pending
        foreach (var sub in _subProcesses)
        {
            var openLog = sub.GetOpenLog();
            if (openLog != null) openLog.End();
            if (resetTime) sub.ResetDuration();
            else if (sub.Status != Enums.SubProcessStatus.Pending)
                sub.ReturnToPending();
        }

        UpdatedAt = DateTime.UtcNow;
    }

    public void AddDuration(int minutes)
    {
        TotalDurationMinutes += minutes;
        UpdatedAt = DateTime.UtcNow;
    }

    public void OverrideComplexity(ComplexityType complexity)
    {
        Complexity = complexity;
        ComplexityOverridden = true;
        UpdatedAt = DateTime.UtcNow;
    }

    public OrderItemSubProcess AddSubProcess(Guid subProcessId)
    {
        if (_subProcesses.Any(sp => sp.SubProcessId == subProcessId))
            throw new DomainException("DUPLICATE_SUBPROCESS", "Sub-process already added.");

        var subProcess = OrderItemSubProcess.Create(TenantId, Id, subProcessId);
        _subProcesses.Add(subProcess);
        return subProcess;
    }

    public void SetExcludedFromReports(bool excluded)
    {
        if (IsExcludedFromReports == excluded) return;
        IsExcludedFromReports = excluded;
        UpdatedAt = DateTime.UtcNow;
    }
}
