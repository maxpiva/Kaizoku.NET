using KaizokuBackend.Data;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace KaizokuBackend.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260321120000_AddProviderHealthCheckColumns")]
public partial class AddProviderHealthCheckColumns : Microsoft.EntityFrameworkCore.Migrations.Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "LastHealthCheckUtc",
            table: "Providers",
            type: "TEXT",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "LastHealthCheckPassed",
            table: "Providers",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LastHealthCheckError",
            table: "Providers",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "LastHealthCheckUtc",
            table: "Providers");

        migrationBuilder.DropColumn(
            name: "LastHealthCheckPassed",
            table: "Providers");

        migrationBuilder.DropColumn(
            name: "LastHealthCheckError",
            table: "Providers");
    }
}
