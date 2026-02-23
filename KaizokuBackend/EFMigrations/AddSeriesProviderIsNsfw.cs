using KaizokuBackend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace KaizokuBackend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260220120000_AddSeriesProviderIsNsfw")]
public partial class AddSeriesProviderIsNsfw : Microsoft.EntityFrameworkCore.Migrations.Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"SeriesProviders\" ADD COLUMN IF NOT EXISTS \"IsNSFW\" INTEGER NOT NULL DEFAULT 0;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("ALTER TABLE \"SeriesProviders\" DROP COLUMN IF EXISTS \"IsNSFW\";");
    }
}