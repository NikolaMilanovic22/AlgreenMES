using MediatR;

namespace AlGreenMES.Modules.Production.Application.Commands.ImportMaterials;

/// <summary>
/// Bulk import of materials. FE parses the xlsx client-side (exceljs)
/// and sends a normalised array. Handler creates each row independently;
/// errors are collected and returned so the operator can fix and re-upload
/// only the bad rows.
/// </summary>
public record ImportMaterialsCommand(
    Guid TenantId,
    IReadOnlyList<ImportMaterialItem> Items,
    Guid? CreatedByUserId) : IRequest<ImportMaterialsResult>;

public record ImportMaterialItem(
    string Code,
    string Name,
    string Unit,
    string Category,
    int MinQuantity,
    int MaxQuantity,
    decimal? DimensionX,
    decimal? DimensionY,
    decimal? DimensionZ,
    string? Location,
    string? Notes);

public record ImportMaterialsResult(
    int Created,
    IReadOnlyList<ImportMaterialError> Errors);

public record ImportMaterialError(int RowIndex, string Code, string Reason);
