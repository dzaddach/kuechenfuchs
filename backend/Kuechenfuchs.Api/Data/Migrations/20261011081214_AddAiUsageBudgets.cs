using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuechenfuchs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageBudgets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiUsageBudgets",
                columns: table => new
                {
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthStart = table.Column<DateOnly>(type: "date", nullable: false),
                    RequestCount = table.Column<int>(type: "integer", nullable: false),
                    SpentUsd = table.Column<decimal>(type: "numeric(14,8)", precision: 14, scale: 8, nullable: false),
                    ReservedUsd = table.Column<decimal>(type: "numeric(14,8)", precision: 14, scale: 8, nullable: false),
                    ReservationUpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageBudgets", x => new { x.HouseholdId, x.MonthStart });
                    table.ForeignKey(
                        name: "FK_AiUsageBudgets_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsageBudgets");
        }
    }
}
