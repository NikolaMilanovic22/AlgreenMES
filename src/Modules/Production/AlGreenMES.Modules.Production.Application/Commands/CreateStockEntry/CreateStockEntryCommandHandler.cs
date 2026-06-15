using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.BuildingBlocks.Common.Interfaces;
using AlGreenMES.Modules.Production.Application.DTOs;
using AlGreenMES.Modules.Production.Application.Interfaces;
using AlGreenMES.Modules.Production.Domain.Entities;
using AlGreenMES.Modules.Production.Domain.Enums;
using AlGreenMES.Modules.Production.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Commands.CreateStockEntry;

public class CreateStockEntryCommandHandler : IRequestHandler<CreateStockEntryCommand, IReadOnlyList<StockMovementDto>>
{
    private readonly IMaterialRepository _materialRepo;
    private readonly IStockMovementRepository _stockRepo;
    private readonly IProductionUnitOfWork _unitOfWork;
    private readonly INotificationCreator _notificationCreator;

    public CreateStockEntryCommandHandler(
        IMaterialRepository materialRepo,
        IStockMovementRepository stockRepo,
        IProductionUnitOfWork unitOfWork,
        INotificationCreator notificationCreator)
    {
        _materialRepo = materialRepo;
        _stockRepo = stockRepo;
        _unitOfWork = unitOfWork;
        _notificationCreator = notificationCreator;
    }

    public async Task<IReadOnlyList<StockMovementDto>> Handle(CreateStockEntryCommand request, CancellationToken cancellationToken)
    {
        if (request.Lines is null || request.Lines.Count == 0)
            throw new DomainException("STOCK_LINES_EMPTY", "Najmanje jedna stavka materijala je obavezna.");

        var materialIds = request.Lines.Select(l => l.MaterialId).Distinct().ToList();
        var materials = (await _materialRepo.GetByIdsAsync(materialIds, cancellationToken))
            .ToDictionary(m => m.Id);

        var missing = materialIds.Where(id => !materials.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            throw new NotFoundException("Material", missing[0]);

        // Capture pre-Outflow on-hand so we can detect "crossing below min"
        // after the save and emit one low-stock notification per crossing.
        Dictionary<Guid, decimal>? quantitiesBefore = null;
        Dictionary<Guid, decimal>? outflowByMaterial = null;

        // Izlaz: refuse to take stock below zero. No LOTs / no FIFO yet
        // (Saša 08.06.2026), but "nedostaje na stanju" still applies.
        if (request.Type == StockMovementType.Outflow)
        {
            outflowByMaterial = request.Lines
                .GroupBy(l => l.MaterialId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
            var available = await _stockRepo.GetQuantitiesAsync(
                request.TenantId, outflowByMaterial.Keys.ToList(), cancellationToken);
            quantitiesBefore = outflowByMaterial.Keys.ToDictionary(id =>
                id, id => available.TryGetValue(id, out var v) ? v : 0m);

            foreach (var (matId, qtyRequested) in outflowByMaterial)
            {
                var onHand = quantitiesBefore[matId];
                if (onHand < qtyRequested)
                {
                    var m = materials[matId];
                    throw new DomainException(
                        "STOCK_INSUFFICIENT",
                        $"Nedovoljno na stanju za '{m.Code} — {m.Name}': trenutno {onHand} {m.Unit}, traženo {qtyRequested} {m.Unit}.");
                }
            }
        }

        // For Izlaz: if UnitPrice not provided, fall back to latest entered
        // unit price per Saša 08.06.2026 ("totalValue ide uvek zadnja").
        var movements = new List<StockMovement>(request.Lines.Count);
        var dtos = new List<StockMovementDto>(request.Lines.Count);

        foreach (var line in request.Lines)
        {
            var material = materials[line.MaterialId];
            var unitPrice = line.UnitPrice
                ?? await _stockRepo.GetLatestUnitPriceAsync(request.TenantId, line.MaterialId, cancellationToken)
                ?? throw new DomainException("STOCK_PRICE_MISSING",
                    $"TotalValue za materijal '{material.Code}' nije navedena, a nema ranijih ulaza iz kojih bi se preuzela.");

            var movement = StockMovement.Create(
                request.TenantId,
                line.MaterialId,
                request.Type,
                line.Quantity,
                unitPrice,
                request.MovementDate,
                request.DocumentReference,
                line.Notes ?? request.Notes,
                request.CreatedByUserId,
                request.ProcessId);

            await _stockRepo.AddAsync(movement, cancellationToken);
            movements.Add(movement);
            dtos.Add(new StockMovementDto(
                movement.Id, material.Id, material.Code, material.Name, material.Unit,
                material.Category, material.DimensionX, material.DimensionY, material.DimensionZ,
                movement.Type, movement.Quantity, movement.UnitPrice, movement.TotalPrice,
                movement.MovementDate, movement.DocumentReference, movement.Notes, movement.CreatedAt,
                movement.ProcessId, null));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // After-save: if this Izlaz pushed any material from at-or-above min
        // down to below min, emit one MaterialLowStock notification per
        // crossing. Materials already below min are silent (no spam on every
        // follow-up Izlaz).
        //
        // We send Title/Message in Serbian as a fallback (matches existing
        // DomainException convention) AND a structured ParamsJson payload —
        // FE prefers rendering via i18n template keyed on Type using the
        // params and only falls back to Title/Message when no template exists.
        if (request.Type == StockMovementType.Outflow && quantitiesBefore is not null && outflowByMaterial is not null)
        {
            // NotificationType.MaterialLowStock = 9 (last enum entry, see
            // Orders.Domain.Enums.NotificationType). Passed as int because
            // Production cannot reference Orders types.
            const int materialLowStockType = 9;
            foreach (var (matId, qtyTaken) in outflowByMaterial)
            {
                var before = quantitiesBefore[matId];
                var after = before - qtyTaken;
                var material = materials[matId];
                if (before >= material.MinQuantity && after < material.MinQuantity)
                {
                    var paramsJson = System.Text.Json.JsonSerializer.Serialize(new
                    {
                        materialId = material.Id,
                        code = material.Code,
                        name = material.Name,
                        unit = material.Unit,
                        onHand = after,
                        min = material.MinQuantity,
                    });
                    await _notificationCreator.NotifyManagementAsync(
                        request.TenantId,
                        materialLowStockType,
                        $"Materijal ispod minimuma: {material.Code} — {material.Name}",
                        $"Stanje materijala '{material.Code} — {material.Name}' je palo ispod minimuma ({after} {material.Unit}, min {material.MinQuantity} {material.Unit}).",
                        "Material",
                        material.Id,
                        paramsJson,
                        cancellationToken);
                }
            }
        }

        return dtos;
    }
}
