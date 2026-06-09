using AlGreenMES.BuildingBlocks.Common.Exceptions;
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

    public CreateStockEntryCommandHandler(
        IMaterialRepository materialRepo,
        IStockMovementRepository stockRepo,
        IProductionUnitOfWork unitOfWork)
    {
        _materialRepo = materialRepo;
        _stockRepo = stockRepo;
        _unitOfWork = unitOfWork;
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

        // Izlaz: refuse to take stock below zero. No LOTs / no FIFO yet
        // (Saša 08.06.2026), but "nedostaje na stanju" still applies.
        if (request.Type == StockMovementType.Outflow)
        {
            var requestedByMaterial = request.Lines
                .GroupBy(l => l.MaterialId)
                .ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));
            var available = await _stockRepo.GetQuantitiesAsync(
                request.TenantId, requestedByMaterial.Keys.ToList(), cancellationToken);

            foreach (var (matId, qtyRequested) in requestedByMaterial)
            {
                var onHand = available.TryGetValue(matId, out var v) ? v : 0m;
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
                request.CreatedByUserId);

            await _stockRepo.AddAsync(movement, cancellationToken);
            movements.Add(movement);
            dtos.Add(new StockMovementDto(
                movement.Id, material.Id, material.Code, material.Name, material.Unit,
                material.Category, material.DimensionX, material.DimensionY, material.DimensionZ,
                movement.Type, movement.Quantity, movement.UnitPrice, movement.TotalPrice,
                movement.MovementDate, movement.DocumentReference, movement.Notes, movement.CreatedAt));
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return dtos;
    }
}
