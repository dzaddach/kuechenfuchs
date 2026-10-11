using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuechenfuchs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAiUsageReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AiUsageReservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    MonthStart = table.Column<DateOnly>(type: "date", nullable: false),
                    ReservedUsd = table.Column<decimal>(type: "numeric(14,8)", precision: 14, scale: 8, nullable: false),
                    FinalizedUsd = table.Column<decimal>(type: "numeric(14,8)", precision: 14, scale: 8, nullable: true),
                    IsEstimated = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AiUsageReservations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AiUsageReservations_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AiUsageReservations_HouseholdId_MonthStart_FinalizedUsd_Cre~",
                table: "AiUsageReservations",
                columns: new[] { "HouseholdId", "MonthStart", "FinalizedUsd", "CreatedAt" });

            migrationBuilder.Sql(
                """
                INSERT INTO "AiUsageReservations"
                    ("Id", "HouseholdId", "MonthStart", "ReservedUsd", "FinalizedUsd", "IsEstimated", "CreatedAt")
                SELECT gen_random_uuid(), "HouseholdId", "MonthStart", "ReservedUsd", NULL, false,
                       COALESCE("ReservationUpdatedAt", CURRENT_TIMESTAMP)
                FROM "AiUsageBudgets"
                WHERE "ReservedUsd" > 0
                """);

            migrationBuilder.DropColumn(
                name: "ReservationUpdatedAt",
                table: "AiUsageBudgets");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AiUsageReservations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReservationUpdatedAt",
                table: "AiUsageBudgets",
                type: "timestamp with time zone",
                nullable: true);
        }
    }
}
