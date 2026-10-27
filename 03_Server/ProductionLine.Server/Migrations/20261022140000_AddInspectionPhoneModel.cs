using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ProductionLine.Server.Data;

#nullable disable

namespace ProductionLine.Server.Migrations;

// 검사 기록에 휴대폰 기종 칸(PhoneModel) 추가 (김하균). 예전 기록은 비어 있음(null)으로 남습니다.
[DbContext(typeof(AppDbContext))]
[Migration("20261022140000_AddInspectionPhoneModel")]
public sealed class AddInspectionPhoneModel : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PhoneModel",
            table: "Inspections",
            type: "TEXT",
            maxLength: 100,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "PhoneModel", table: "Inspections");
    }
}
