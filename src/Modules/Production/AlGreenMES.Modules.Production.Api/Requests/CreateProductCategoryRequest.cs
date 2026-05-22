using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Production.Api.Requests;

public record CreateProductCategoryRequest(
    string Name,
    string? Description,
    int? DefaultWarningDays,
    int? DefaultCriticalDays,
    List<CategoryProcessInput>? Processes,
    List<CategoryDependencyInput>? Dependencies);

public record CategoryProcessInput(
    Guid ProcessId,
    int SequenceOrder,
    ComplexityType? DefaultComplexity);

public record CategoryDependencyInput(
    Guid ProcessId,
    Guid DependsOnProcessId);
