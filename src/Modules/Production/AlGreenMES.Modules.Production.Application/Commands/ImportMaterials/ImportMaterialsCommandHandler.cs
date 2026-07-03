using AlGreenMES.BuildingBlocks.Common.Exceptions;
using AlGreenMES.Modules.Production.Application.Interfaces;
using AlGreenMES.Modules.Production.Domain.Entities;
using AlGreenMES.Modules.Production.Domain.Repositories;
using MediatR;

namespace AlGreenMES.Modules.Production.Application.Commands.ImportMaterials;

public class ImportMaterialsCommandHandler : IRequestHandler<ImportMaterialsCommand, ImportMaterialsResult>
{
    private readonly IMaterialRepository _repo;
    private readonly IProductionUnitOfWork _unitOfWork;

    public ImportMaterialsCommandHandler(IMaterialRepository repo, IProductionUnitOfWork unitOfWork)
    {
        _repo = repo;
        _unitOfWork = unitOfWork;
    }

    public async Task<ImportMaterialsResult> Handle(ImportMaterialsCommand request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
            throw new DomainException("IMPORT_EMPTY", "Lista materijala je prazna.");

        var errors = new List<ImportMaterialError>();
        var seenInBatch = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var created = 0;

        // Preload existing codes once instead of one EXISTS query per row (N+1 on
        // bulk import). Ordinal (case-sensitive) to match ExistsByKodAsync's
        // `m.Code == trimmed` exact comparison.
        var existingCodes = new HashSet<string>(
            (await _repo.GetByTenantIdAsync(request.TenantId, cancellationToken)).Select(m => m.Code),
            StringComparer.Ordinal);

        for (var i = 0; i < request.Items.Count; i++)
        {
            var item = request.Items[i];
            var rowIndex = i + 1;
            var trimmedCode = (item.Code ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(trimmedCode))
            {
                errors.Add(new ImportMaterialError(rowIndex, item.Code ?? string.Empty, "Kod je prazan."));
                continue;
            }

            if (!seenInBatch.Add(trimmedCode))
            {
                errors.Add(new ImportMaterialError(rowIndex, trimmedCode, "Duplikat koda u istom uvozu."));
                continue;
            }

            if (existingCodes.Contains(trimmedCode))
            {
                errors.Add(new ImportMaterialError(rowIndex, trimmedCode, "Materijal sa istim kodom već postoji."));
                continue;
            }

            try
            {
                var material = Material.Create(
                    request.TenantId,
                    trimmedCode,
                    item.Name,
                    item.Unit,
                    item.Category,
                    item.MinQuantity,
                    item.MaxQuantity,
                    item.DimensionX,
                    item.DimensionY,
                    item.DimensionZ,
                    item.Location,
                    item.Notes,
                    request.CreatedByUserId);

                await _repo.AddAsync(material, cancellationToken);
                created++;
            }
            catch (DomainException ex)
            {
                errors.Add(new ImportMaterialError(rowIndex, trimmedCode, ex.Message));
            }
        }

        if (created > 0)
            await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ImportMaterialsResult(created, errors);
    }
}
