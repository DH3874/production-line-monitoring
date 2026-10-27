using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ProductionLine.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddIssueMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IssueMessages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IssueId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SenderId = table.Column<string>(type: "TEXT", nullable: false),
                    SenderName = table.Column<string>(type: "TEXT", nullable: false),
                    SenderRole = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IssueMessages", x => x.Id);
                    table.CheckConstraint("CK_IssueMessages_MessageLength", "length(\"Message\") BETWEEN 1 AND 2000");
                    table.ForeignKey(
                        name: "FK_IssueMessages_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_IssueMessages_Users_SenderId",
                        column: x => x.SenderId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_IssueMessages_IssueId_Id",
                table: "IssueMessages",
                columns: new[] { "IssueId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_IssueMessages_SenderId",
                table: "IssueMessages",
                column: "SenderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "IssueMessages");
        }
    }
}
