using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Kuechenfuchs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRecipes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Foods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Foods", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Recipes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(90)", maxLength: 90, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    BaseServings = table.Column<int>(type: "integer", nullable: false),
                    PreparationMinutes = table.Column<int>(type: "integer", nullable: false),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Tags = table.Column<string[]>(type: "text[]", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Recipes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Recipes_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Units",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Dimension = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Units", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "RecipeSteps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeSteps_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RecipeIngredients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    UnitCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    FoodId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeIngredients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Foods_FoodId",
                        column: x => x.FoodId,
                        principalTable: "Foods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeIngredients_Units_UnitCode",
                        column: x => x.UnitCode,
                        principalTable: "Units",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Foods",
                columns: new[] { "Id", "Category", "Name" },
                values: new object[,]
                {
                    { new Guid("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc001"), "Trockenwaren", "Nudeln" },
                    { new Guid("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc002"), "Gemüse", "Tomaten" },
                    { new Guid("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc003"), "Gemüse", "Zwiebeln" },
                    { new Guid("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc004"), "Backen", "Mehl" },
                    { new Guid("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc005"), "Käse", "Parmesan" },
                    { new Guid("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc006"), "Öl und Essig", "Olivenöl" },
                    { new Guid("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc007"), "Milchprodukte", "Naturjoghurt" },
                    { new Guid("e6a9a2a5-a9e5-4be2-bcdf-31d3c55bc008"), "Gemüse", "Karotten" }
                });

            migrationBuilder.InsertData(
                table: "Units",
                columns: new[] { "Code", "Dimension", "Name" },
                values: new object[,]
                {
                    { "g", "mass", "Gramm" },
                    { "kg", "mass", "Kilogramm" },
                    { "l", "volume", "Liter" },
                    { "ml", "volume", "Milliliter" },
                    { "piece", "count", "Stück" },
                    { "pinch", "count", "Prise" },
                    { "tbsp", "volume", "Esslöffel" },
                    { "tsp", "volume", "Teelöffel" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Foods_Name",
                table: "Foods",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_FoodId",
                table: "RecipeIngredients",
                column: "FoodId");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_RecipeId_Position",
                table: "RecipeIngredients",
                columns: new[] { "RecipeId", "Position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecipeIngredients_UnitCode",
                table: "RecipeIngredients",
                column: "UnitCode");

            migrationBuilder.CreateIndex(
                name: "IX_Recipes_HouseholdId_Name",
                table: "Recipes",
                columns: new[] { "HouseholdId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_RecipeSteps_RecipeId_Position",
                table: "RecipeSteps",
                columns: new[] { "RecipeId", "Position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecipeIngredients");

            migrationBuilder.DropTable(
                name: "RecipeSteps");

            migrationBuilder.DropTable(
                name: "Foods");

            migrationBuilder.DropTable(
                name: "Units");

            migrationBuilder.DropTable(
                name: "Recipes");
        }
    }
}
