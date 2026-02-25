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
        migrationBuilder.AddColumn<bool>(
            name: "IsNSFW",
            table: "SeriesProviders",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "IsNSFW",
            table: "SeriesProviders");
    }
}
