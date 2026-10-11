using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuechenfuchs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipeFavorites : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Recipes_HouseholdId_Id",
                table: "Recipes",
                columns: new[] { "HouseholdId", "Id" });

            migrationBuilder.CreateTable(
                name: "RecipeFavorites",
                columns: table => new
                {
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeFavorites", x => new { x.HouseholdId, x.RecipeId });
                    table.ForeignKey(
                        name: "FK_RecipeFavorites_Recipes_HouseholdId_RecipeId",
                        columns: x => new { x.HouseholdId, x.RecipeId },
                        principalTable: "Recipes",
                        principalColumns: new[] { "HouseholdId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecipeFavorites");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Recipes_HouseholdId_Id",
                table: "Recipes");
        }
    }
}
