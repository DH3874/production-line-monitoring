using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProductionLine.Server.Data;

#nullable disable

namespace ProductionLine.Server.Migrations;

// 센서·벨트 기록 표(LineHardwareLogs) 추가 (김하균)
[DbContext(typeof(AppDbContext))]
[Migration("20261022120000_AddLineHardwareLogs")]
public sealed class AddLineHardwareLogs : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "LineHardwareLogs",
            columns: table => new
            {
                Id = table.Column<long>(type: "INTEGER", nullable: false)
                    .Annotation("Sqlite:Autoincrement", true),
                LineId = table.Column<int>(type: "INTEGER", nullable: false),
                RecordedAt = table.Column<long>(type: "INTEGER", nullable: false),
                Reason = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Connected = table.Column<bool>(type: "INTEGER", nullable: false),
                TemperatureCelsius = table.Column<double>(type: "REAL", nullable: true),
                HumidityPercent = table.Column<double>(type: "REAL", nullable: true),
                GasDetected = table.Column<bool>(type: "INTEGER", nullable: true),
                GasAnalogValue = table.Column<int>(type: "INTEGER", nullable: true),
                GasWarmingUp = table.Column<bool>(type: "INTEGER", nullable: false),
                EmergencyStopped = table.Column<bool>(type: "INTEGER", nullable: false),
                BeltRunning = table.Column<bool>(type: "INTEGER", nullable: false),
                SpeedBoosted = table.Column<bool>(type: "INTEGER", nullable: false),
                SorterAngle = table.Column<int>(type: "INTEGER", nullable: false),
                Sorting = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_LineHardwareLogs", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "IX_LineHardwareLogs_LineId_RecordedAt",
            table: "LineHardwareLogs",
            columns: new[] { "LineId", "RecordedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_LineHardwareLogs_RecordedAt",
            table: "LineHardwareLogs",
            column: "RecordedAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "LineHardwareLogs");
    }
}
