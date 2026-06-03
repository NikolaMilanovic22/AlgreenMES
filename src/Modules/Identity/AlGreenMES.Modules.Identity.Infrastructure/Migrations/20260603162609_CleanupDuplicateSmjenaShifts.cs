using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AlGreenMES.Modules.Identity.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CleanupDuplicateSmjenaShifts : Migration
    {
        /// <summary>
        /// One-time cleanup (Bojan 03.06.2026 — duplicate shifts in admin).
        /// The original DataSeeder used ijekavica ("smjena"); a 29.05.2026
        /// fix changed it to ekavica ("smena"). Because the seeder dedupes by
        /// Name, the next boot created the new "smena" rows alongside the
        /// existing "smjena" ones → duplicates. WorkSession has no FK to
        /// Shift (matched by time-of-day), so deleting these rows is safe.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DELETE FROM identity.shifts WHERE name IN ('Jutarnja smjena', 'Popodnevna smjena', 'Noćna smjena');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op: we don't restore the buggy duplicate names on rollback.
        }
    }
}
