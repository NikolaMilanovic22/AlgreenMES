using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;

namespace AlGreenMES.Modules.Production.Domain.Entities;

/// <summary>
/// Warehouse material master record. Saša Excel spec 08.06.2026 ("Lista
/// materijala"). Code is unique per tenant (Saša confirmed — the duplicate
/// in the example was a typo). Stanje (current quantity) is computed from
/// <c>StockMovement</c> sums, not stored on this row, to avoid drift.
///
/// Dimensions X/Y/Z + Location are optional (e.g. glass has no dimensions
/// in the example). MinQuantity/MaxQuantity drive the "ISPOD MIN" /
/// "IZNAD MAX" status flags shown on the Stanje page.
/// </summary>
public class Material : AuditableEntity
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Unit { get; private set; } = null!;
    public decimal? DimensionX { get; private set; }
    public decimal? DimensionY { get; private set; }
    public decimal? DimensionZ { get; private set; }
    public string Category { get; private set; } = null!;
    public int MinQuantity { get; private set; }
    public int MaxQuantity { get; private set; }
    public string? Location { get; private set; }
    public string? Notes { get; private set; }
    public bool IsActive { get; private set; } = true;

    private Material() { }

    public static Material Create(
        Guid tenantId,
        string code,
        string name,
        string unit,
        string category,
        int minQuantity,
        int maxQuantity,
        decimal? dimensionX = null,
        decimal? dimensionY = null,
        decimal? dimensionZ = null,
        string? location = null,
        string? notes = null,
        Guid? createdByUserId = null)
    {
        ValidateRequired(code, "MATERIAL_CODE_REQUIRED", "Kod materijala je obavezan.");
        ValidateRequired(name, "MATERIAL_NAME_REQUIRED", "Naziv materijala je obavezan.");
        ValidateRequired(unit, "MATERIAL_UNIT_REQUIRED", "Jedinica mere je obavezna.");
        ValidateRequired(category, "MATERIAL_CATEGORY_REQUIRED", "Kategorija je obavezna.");
        ValidateThresholds(minQuantity, maxQuantity);

        var material = new Material
        {
            TenantId = tenantId,
            Code = code.Trim(),
            Name = name.Trim(),
            Unit = unit.Trim(),
            Category = category.Trim(),
            MinQuantity = minQuantity,
            MaxQuantity = maxQuantity,
            DimensionX = dimensionX,
            DimensionY = dimensionY,
            DimensionZ = dimensionZ,
            Location = location?.Trim(),
            Notes = notes?.Trim()
        };
        if (createdByUserId.HasValue)
            material.SetCreated(createdByUserId.Value);
        return material;
    }

    public void Update(
        string name,
        string unit,
        string category,
        int minQuantity,
        int maxQuantity,
        decimal? dimensionX,
        decimal? dimensionY,
        decimal? dimensionZ,
        string? location,
        string? notes)
    {
        ValidateRequired(name, "MATERIAL_NAME_REQUIRED", "Naziv materijala je obavezan.");
        ValidateRequired(unit, "MATERIAL_UNIT_REQUIRED", "Jedinica mere je obavezna.");
        ValidateRequired(category, "MATERIAL_CATEGORY_REQUIRED", "Kategorija je obavezna.");
        ValidateThresholds(minQuantity, maxQuantity);

        Name = name.Trim();
        Unit = unit.Trim();
        Category = category.Trim();
        MinQuantity = minQuantity;
        MaxQuantity = maxQuantity;
        DimensionX = dimensionX;
        DimensionY = dimensionY;
        DimensionZ = dimensionZ;
        Location = location?.Trim();
        Notes = notes?.Trim();
    }

    public void Deactivate() => IsActive = false;
    public void Reactivate() => IsActive = true;

    private static void ValidateRequired(string value, string code, string message)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new DomainException(code, message);
    }

    private static void ValidateThresholds(int min, int max)
    {
        if (min < 0) throw new DomainException("MATERIAL_MIN_NEGATIVE", "Min količina ne sme biti negativna.");
        if (max < 0) throw new DomainException("MATERIAL_MAX_NEGATIVE", "Max količina ne sme biti negativna.");
        if (max > 0 && min > max)
            throw new DomainException("MATERIAL_MIN_GT_MAX", "Min količina ne sme biti veća od Max količine.");
    }
}
