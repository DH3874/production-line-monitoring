using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLine.Server.Migrations;

// 안 쓰는 형태·색상 점수 칸 삭제 (김하균). SQLite는 EF가 표를 새로 만들어 옮기므로 다른 칸의 데이터는 그대로 남습니다.
// 표를 새로 만들 때 필요한 "지운 뒤 표 모양"은 같은 이름의 .Designer.cs 파일에 있습니다.
public partial class RemoveInspectionScores : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ShapeScore", table: "Inspections");
        migrationBuilder.DropColumn(name: "ColorScore", table: "Inspections");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<double>(name: "ShapeScore", table: "Inspections", type: "REAL", nullable: false, defaultValue: 0.0);
        migrationBuilder.AddColumn<double>(name: "ColorScore", table: "Inspections", type: "REAL", nullable: false, defaultValue: 0.0);
    }
}
