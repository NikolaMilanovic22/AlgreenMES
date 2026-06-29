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

    /// <summary>
    /// The worker who STARTED this process (Bojan 04.06.2026 — needed so
    /// process-level work, i.e. processes without sub-processes like Krojenje,
    /// can be attributed per-user in the Sati radnika report). For processes
    /// with sub-processes the per-user time still lives on subprocess logs;
    /// this field only matters for processes-without-subprocesses. Set at
    /// StartProcessWork; resumed-from-pause keeps the original starter
    /// (the typical case is the same worker resuming). Null on historical
    /// rows that pre-date the column.
    /// </summary>
    public Guid? StartedByUserId { get; private set; }

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
    /// When set, this process was paused by a tablet logout and should
    /// auto-resume on the next worker login (ResumeOnLoginCommand).
    /// Null means either "not paused" or "paused manually by a worker"
    /// (which must not auto-resume).
    /// </summary>
    public DateTime? PausedOnLogoutAt { get; private set; }

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

    /// <summary>
    /// Per-work-period logs for process-level work (only populated for
    /// processes WITHOUT sub-processes — those with sub-processes use
    /// OrderItemSubProcessLog instead). Bojan 06.06.2026: tracks each
    /// Start→Pause/Resume cycle so Aktivno / Nepokriveno correctly excludes
    /// offline gaps and mid-session pauses.
    /// </summary>
    private readonly List<OrderItemProcessLog> _processLogs = new();
    public IReadOnlyCollection<OrderItemProcessLog> ProcessLogs => _processLogs.AsReadOnly();

    private void EndOpenProcessLog(DateTime? occurredAt = null)
    {
        var open = _processLogs.FirstOrDefault(l => l.EndTime == null);
        open?.End(occurredAt);
    }

    private void StartProcessLog(Guid userId, DateTime? occurredAt = null)
    {
        // Defensive: end any stray open log first (shouldn't normally happen).
        EndOpenProcessLog(occurredAt);
        _processLogs.Add(OrderItemProcessLog.Start(TenantIdRequired, Id, userId, occurredAt));
    }

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

    public void Start(Guid? startedByUserId = null, DateTime? occurredAt = null)
    {
        if (Status != ProcessStatus.Pending)
            throw new DomainException("INVALID_STATUS", "Can only start pending processes.");
        Status = ProcessStatus.InProgress;
        StartedAt = occurredAt ?? DateTime.UtcNow;
        StartedByUserId = startedByUserId;
        UpdatedAt = DateTime.UtcNow;

        // Process-level work log only kicks in for processes WITHOUT
        // sub-processes — those route their time through subprocess logs
        // (which Start() will trigger separately via StartProcessWork
        // selecting the first sub-process and calling StartLog).
        var hasSubProcesses = _subProcesses.Any(sp => !sp.IsWithdrawn);
        if (!hasSubProcesses && startedByUserId.HasValue)
            StartProcessLog(startedByUserId.Value, occurredAt);
    }

    public void Complete(DateTime? occurredAt = null)
    {
        // Status guard added 23.06.2026 after NegativePathGuardTests
        // discovered that a Pending or Completed process could be silently
        // re-marked Completed (with TotalDurationMinutes=0 in the Pending
        // case, corrupting reports + letting tablet workers fake work
        // they never started). Only InProgress can transition to Completed
        // — Blocked must Unblock first, Pending must Start first.
        if (Status != ProcessStatus.InProgress)
            throw new DomainException("INVALID_STATUS", "Can only complete in-progress processes.");

        if (_subProcesses.Any() && !_subProcesses.All(sp => sp.Status == SubProcessStatus.Completed || sp.Status == SubProcessStatus.Withdrawn))
            throw new DomainException("SUBPROCESSES_NOT_COMPLETE", "All sub-processes must be completed first.");

        // Accumulate current session duration before completing. occurredAt is
        // the real moment work finished (set when a tablet "complete" was taken
        // offline and replayed later); null = now.
        var completionTime = occurredAt ?? DateTime.UtcNow;
        if (!PausedAt.HasValue && (StartedAt.HasValue || ResumedAt.HasValue))
        {
            var sessionStart = ResumedAt ?? StartedAt ?? completionTime;
            var sessionSeconds = (int)(completionTime - sessionStart).TotalSeconds;
            TotalDurationMinutes += sessionSeconds;
        }

        Status = ProcessStatus.Completed;
        CompletedAt = completionTime;
        UpdatedAt = DateTime.UtcNow;
        EndOpenProcessLog(occurredAt);
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
        EndOpenProcessLog();
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
        EndOpenProcessLog();
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
        EndOpenProcessLog();
    }

    public void Pause(DateTime? occurredAt = null)
    {
        if (PausedAt.HasValue)
            throw new DomainException("ALREADY_PAUSED", "Process is already paused.");

        // Accumulate current session duration in seconds. occurredAt is the
        // real moment the worker paused (set when a tablet "stop" was taken
        // offline and replayed later); null = now.
        var pauseTime = occurredAt ?? DateTime.UtcNow;
        var sessionStart = ResumedAt ?? StartedAt ?? pauseTime;
        var sessionSeconds = (int)(pauseTime - sessionStart).TotalSeconds;
        TotalDurationMinutes += sessionSeconds;

        PausedAt = pauseTime;
        PausedOnLogoutAt = null; // manual pause — not auto-resumable on next login
        ResumedAt = null;
        UpdatedAt = DateTime.UtcNow;
        EndOpenProcessLog(occurredAt);
    }

    /// <summary>
    /// Pause because the worker is logging out of the tablet. Sets
    /// PausedOnLogoutAt so the next tablet login auto-resumes this
    /// process. Skips if already paused (manually) — manual pauses must
    /// NOT auto-resume.
    /// </summary>
    public void PauseOnLogout()
    {
        if (PausedAt.HasValue) return;

        var sessionStart = ResumedAt ?? StartedAt ?? DateTime.UtcNow;
        var sessionSeconds = (int)(DateTime.UtcNow - sessionStart).TotalSeconds;
        TotalDurationMinutes += sessionSeconds;

        PausedAt = DateTime.UtcNow;
        PausedOnLogoutAt = DateTime.UtcNow;
        ResumedAt = null;
        UpdatedAt = DateTime.UtcNow;
        EndOpenProcessLog();
    }

    /// <summary>
    /// Mark the parent OIP as auto-logged-out without setting PausedAt.
    /// Used by PauseOnLogoutCommand for sub-process style processes — the
    /// parent OIP's timer doesn't run (sub-process logs carry the time),
    /// so we only need a flag for ResumeOnLogin to distinguish auto-logout
    /// from a manual pause. Saša 08.06.2026 (Bug 2): sub-processes weren't
    /// resuming on OT relogin in some cases because the sub-level
    /// PausedOnLogoutAt didn't make it through the PauseWork → PauseOnLogout
    /// chain — anchoring the marker on the parent is more robust.
    /// </summary>
    public void MarkAutoPausedOnLogout()
    {
        if (PausedOnLogoutAt.HasValue) return;
        PausedOnLogoutAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Clears the auto-logout marker on the parent OIP without touching
    /// PausedAt / ResumedAt / logs. Used by ResumeOnLogin for sub-process
    /// style processes after auto-resuming the active sub-process.
    /// </summary>
    public void ClearAutoLogoutMarker()
    {
        if (!PausedOnLogoutAt.HasValue) return;
        PausedOnLogoutAt = null;
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
        EndOpenProcessLog();

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
        EndOpenProcessLog();

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

    public void ResumeTimer(Guid? resumedByUserId = null)
    {
        if (!PausedAt.HasValue)
            throw new DomainException("NOT_PAUSED", "Process is not paused.");

        PausedAt = null;
        PausedOnLogoutAt = null; // resumed — no longer eligible for auto-resume
        ResumedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;

        // New work period starts now. Only emit a log for processes without
        // sub-processes — those with sub-processes route their resume through
        // OrderItemSubProcess.StartLog.
        var hasSubProcesses = _subProcesses.Any(sp => !sp.IsWithdrawn);
        if (!hasSubProcesses && resumedByUserId.HasValue)
            StartProcessLog(resumedByUserId.Value);
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

        var subProcess = OrderItemSubProcess.Create(TenantIdRequired, Id, subProcessId);
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
