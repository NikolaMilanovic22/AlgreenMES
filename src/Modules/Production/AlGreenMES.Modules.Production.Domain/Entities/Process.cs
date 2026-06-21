using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlGreenMES.Modules.Production.Domain.Entities;

public class Process : AuditableEntity
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int SequenceOrder { get; private set; }
    public bool IsActive { get; private set; } = true;

    private readonly List<SubProcess> _subProcesses = new();
    public IReadOnlyCollection<SubProcess> SubProcesses => _subProcesses.AsReadOnly();

    private Process()
    {
    }

    public static Process Create(Guid tenantId, string code, string name, int sequenceOrder, Guid? createdByUserId = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("INVALID_CODE", "Process code is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Process name is required.");

        var process = new Process
        {
            TenantId = tenantId,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            SequenceOrder = sequenceOrder
        };
        if (createdByUserId.HasValue)
            process.SetCreated(createdByUserId.Value);
        return process;
    }

    public void Update(string code, string name, int sequenceOrder)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("INVALID_CODE", "Process code is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Process name is required.");

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
        SequenceOrder = sequenceOrder;
    }

    public SubProcess AddSubProcess(string name, int sequenceOrder)
    {
        if (_subProcesses.Any(sp => sp.IsActive && sp.SequenceOrder == sequenceOrder))
            throw new DomainException("DUPLICATE_ORDER", $"Sub-process with order {sequenceOrder} already exists.");

        var subProcess = SubProcess.Create(TenantIdRequired, Id, name, sequenceOrder);
        _subProcesses.Add(subProcess);
        return subProcess;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
