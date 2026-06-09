using AlGreenMES.BuildingBlocks.Common.Entities;
using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Production.Domain.Enums;

namespace AlGreenMES.Modules.Production.Domain.Entities;

/// <summary>
/// One row per warehouse inflow (Ulaz) / outflow (Izlaz). The current
/// stock level (Stanje) is computed by summing signed quantities per
/// material — Ulaz adds, Izlaz subtracts. Saša 08.06.2026 (Magacin
/// Excel): kept simple for v1 — no LOTs, single price per movement
/// (always last-entered when querying Izlaz). DocumentReference is the
/// "Broj prijemnice" (Ulaz) or "Broj narudžbenice" (Izlaz) — free text,
/// no FK to Order in this phase per Saša's answer.
/// </summary>
public class StockMovement : AuditableEntity
{
    public Guid MaterialId { get; private set; }
    public StockMovementType Type { get; private set; }
    /// <summary>Always positive. Direction comes from <see cref="Type"/>.</summary>
    public decimal Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal TotalPrice { get; private set; }
    public DateTime MovementDate { get; private set; }
    /// <summary>Prijemnica # for Ulaz, narudžbenica # for Izlaz. Free text v1.</summary>
    public string DocumentReference { get; private set; } = null!;
    public string? Notes { get; private set; }

    /// <summary>
    /// Optional production process the Izlaz is consumed by. Saša 09.06.2026:
    /// optional in v1, will become mandatory in a later phase that auto-links
    /// movements to the corresponding process on the referenced order. Only
    /// makes sense for Outflow; Inflow always sets this to null.
    /// </summary>
    public Guid? ProcessId { get; private set; }

    public Material Material { get; private set; } = null!;
    public Process? Process { get; private set; }

    private StockMovement() { }

    public static StockMovement Create(
        Guid tenantId,
        Guid materialId,
        StockMovementType type,
        decimal quantity,
        decimal unitPrice,
        DateTime movementDate,
        string documentReference,
        string? notes,
        Guid? createdByUserId = null,
        Guid? processId = null)
    {
        if (quantity <= 0) throw new DomainException("STOCK_QTY_INVALID", "Količina mora biti veća od 0.");
        if (unitPrice < 0) throw new DomainException("STOCK_PRICE_NEGATIVE", "TotalValue ne sme biti negativna.");
        if (string.IsNullOrWhiteSpace(documentReference))
            throw new DomainException("STOCK_DOCREF_REQUIRED", "Broj prijemnice/narudžbenice je obavezan.");

        var sm = new StockMovement
        {
            TenantId = tenantId,
            MaterialId = materialId,
            Type = type,
            Quantity = quantity,
            UnitPrice = unitPrice,
            TotalPrice = Math.Round(quantity * unitPrice, 2),
            MovementDate = movementDate == default ? DateTime.UtcNow : movementDate,
            DocumentReference = documentReference.Trim(),
            Notes = notes?.Trim(),
            ProcessId = type == StockMovementType.Outflow ? processId : null,
        };
        if (createdByUserId.HasValue) sm.SetCreated(createdByUserId.Value);
        return sm;
    }

    /// <summary>Signed quantity for Stanje math. Ulaz = +, Izlaz = -.</summary>
    public decimal SignedQuantity => Type == StockMovementType.Inflow ? Quantity : -Quantity;
}
