using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Production.Domain.Entities;

public class ProductCategory : AuditableEntity
{
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public bool IsActive { get; private set; } = true;
    public int? DefaultWarningDays { get; private set; }
    public int? DefaultCriticalDays { get; private set; }

    private readonly List<ProductCategoryProcess> _processes = new();
    public IReadOnlyCollection<ProductCategoryProcess> Processes => _processes.AsReadOnly();

    private readonly List<ProductCategoryDependency> _dependencies = new();
    public IReadOnlyCollection<ProductCategoryDependency> Dependencies => _dependencies.AsReadOnly();

    private ProductCategory()
    {
    }

    public static ProductCategory Create(Guid tenantId, string name, string? description = null, Guid? createdByUserId = null, int? defaultWarningDays = null, int? defaultCriticalDays = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Category name is required.");

        var category = new ProductCategory
        {
            TenantId = tenantId,
            Name = name.Trim(),
            Description = description?.Trim(),
            DefaultWarningDays = defaultWarningDays,
            DefaultCriticalDays = defaultCriticalDays
        };
        if (createdByUserId.HasValue)
            category.SetCreated(createdByUserId.Value);
        return category;
    }

    public void Update(string name, string? description, int? defaultWarningDays = null, int? defaultCriticalDays = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("INVALID_NAME", "Category name is required.");

        Name = name.Trim();
        Description = description?.Trim();
        DefaultWarningDays = defaultWarningDays;
        DefaultCriticalDays = defaultCriticalDays;
    }

    public void AddProcess(Guid processId, int sequenceOrder, ComplexityType? defaultComplexity = null)
    {
        if (_processes.Any(p => p.ProcessId == processId))
            throw new DomainException("DUPLICATE_PROCESS", "Process already added to category.");

        _processes.Add(new ProductCategoryProcess(TenantIdRequired, Id, processId, sequenceOrder, defaultComplexity));
    }

    public void RemoveProcess(Guid processId)
    {
        var process = _processes.FirstOrDefault(p => p.ProcessId == processId)
            ?? throw new NotFoundException("Process not in this category.");

        _dependencies.RemoveAll(d => d.ProcessId == processId || d.DependsOnProcessId == processId);
        _processes.Remove(process);
    }

    public void AddDependency(Guid processId, Guid dependsOnProcessId)
    {
        if (!_processes.Any(p => p.ProcessId == processId))
            throw new DomainException("PROCESS_NOT_IN_CATEGORY", "Process not in this category.");
        if (!_processes.Any(p => p.ProcessId == dependsOnProcessId))
            throw new DomainException("DEPENDENCY_NOT_IN_CATEGORY", "Dependency process not in this category.");

        if (_dependencies.Any(d => d.ProcessId == processId && d.DependsOnProcessId == dependsOnProcessId))
            throw new DomainException("DUPLICATE_DEPENDENCY", "This dependency already exists.");

        if (WouldCreateCircularDependency(processId, dependsOnProcessId))
            throw new DomainException("CIRCULAR_DEPENDENCY", "This would create a circular dependency.");

        _dependencies.Add(new ProductCategoryDependency(TenantIdRequired, Id, processId, dependsOnProcessId));
    }

    public void RemoveDependency(Guid dependencyId)
    {
        var dep = _dependencies.FirstOrDefault(d => d.Id == dependencyId)
            ?? throw new NotFoundException("Dependency not found.");

        _dependencies.Remove(dep);
    }

    public void ReplaceProcesses(IEnumerable<(Guid processId, int sequenceOrder, ComplexityType? defaultComplexity)> processes)
    {
        _processes.Clear();
        _dependencies.Clear();
        foreach (var (processId, sequenceOrder, defaultComplexity) in processes)
            _processes.Add(new ProductCategoryProcess(TenantIdRequired, Id, processId, sequenceOrder, defaultComplexity));
    }

    public void ReplaceDependencies(IEnumerable<(Guid processId, Guid dependsOnProcessId)> dependencies)
    {
        _dependencies.Clear();
        foreach (var (processId, dependsOnProcessId) in dependencies)
            AddDependency(processId, dependsOnProcessId);
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;

    private bool WouldCreateCircularDependency(Guid processId, Guid dependsOnId)
    {
        var visited = new HashSet<Guid>();
        var queue = new Queue<Guid>();
        queue.Enqueue(dependsOnId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == processId) return true;
            if (!visited.Add(current)) continue;

            foreach (var dep in _dependencies.Where(d => d.ProcessId == current))
                queue.Enqueue(dep.DependsOnProcessId);
        }

        return false;
    }
}
