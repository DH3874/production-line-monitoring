using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProductionLine.Server.Data;

#nullable disable

namespace ProductionLine.Server.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261019183000_AddCameraInspectionFields")]
public sealed class AddCameraInspectionFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(
            name: "DetectionConfidence",
            table: "Inspections",
            type: "REAL",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "HasImage",
            table: "Inspections",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DetectionConfidence", table: "Inspections");
        migrationBuilder.DropColumn(name: "HasImage", table: "Inspections");
    }
}
