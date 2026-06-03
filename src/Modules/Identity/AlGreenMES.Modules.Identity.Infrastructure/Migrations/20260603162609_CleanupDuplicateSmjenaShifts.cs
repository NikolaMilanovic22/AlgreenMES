using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CleanupDuplicateSmjenaShifts : Migration
    {
        /// <summary>
        /// One-time cleanup (Bojan 03.06.2026 — duplicate shifts in admin).
        /// Original DataSeeder used ijekavica ("smjena"); a 29.05.2026 fix
        /// switched it to ekavica ("smena") but the seeder dedupes by name,
        /// so the next boot added "smena" alongside the existing "smjena" →
        /// duplicates per tenant. Per-tenant: if the ekavica counterpart
        /// already exists drop the ijekavica row, otherwise rename it. That
        /// way DBs which never got the new seed (e.g. local dev with the
        /// original ijekavica only) keep their shifts after migration.
        /// WorkSession has no FK to Shift (matched by time-of-day), so both
        /// the delete and rename are safe for downstream data.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM identity.shifts s_old
                WHERE s_old.name IN ('Jutarnja smjena', 'Popodnevna smjena', 'Noćna smjena')
                  AND EXISTS (
                    SELECT 1 FROM identity.shifts s_new
                    WHERE s_new.tenant_id = s_old.tenant_id
                      AND s_new.name = REPLACE(s_old.name, 'smjena', 'smena')
                  );

                UPDATE identity.shifts
                SET name = REPLACE(name, 'smjena', 'smena')
                WHERE name IN ('Jutarnja smjena', 'Popodnevna smjena', 'Noćna smjena');
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: we don't restore the buggy duplicate names on rollback.
        }
    }
}
