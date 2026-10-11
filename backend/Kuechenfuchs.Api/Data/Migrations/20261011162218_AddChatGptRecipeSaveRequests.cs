using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuechenfuchs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChatGptRecipeSaveRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatGptRecipeSaveRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    PayloadHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConfirmedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RecipeId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatGptRecipeSaveRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatGptRecipeSaveRequests_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatGptRecipeSaveRequests_ExternalIdentityLinks_LinkId",
                        column: x => x.LinkId,
                        principalTable: "ExternalIdentityLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatGptRecipeSaveRequests_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatGptRecipeSaveRequests_Recipes_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "Recipes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptRecipeSaveRequests_ExpiresAt",
                table: "ChatGptRecipeSaveRequests",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptRecipeSaveRequests_HouseholdId",
                table: "ChatGptRecipeSaveRequests",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptRecipeSaveRequests_LinkId_ConfirmedAt_ExpiresAt",
                table: "ChatGptRecipeSaveRequests",
                columns: new[] { "LinkId", "ConfirmedAt", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptRecipeSaveRequests_RecipeId",
                table: "ChatGptRecipeSaveRequests",
                column: "RecipeId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatGptRecipeSaveRequests_UserId",
                table: "ChatGptRecipeSaveRequests",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatGptRecipeSaveRequests");
        }
    }
}
