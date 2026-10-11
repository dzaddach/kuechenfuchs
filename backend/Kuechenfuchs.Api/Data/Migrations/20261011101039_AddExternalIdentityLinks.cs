using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuechenfuchs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalIdentityLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ExternalIdentityLinkRequests",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    StateHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    NonceHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    BrowserBindingHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    ProtectedCodeVerifier = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentityLinkRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIdentityLinkRequests_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExternalIdentityLinkRequests_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ExternalIdentityLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Issuer = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Subject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    HouseholdId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    RevokedReason = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalIdentityLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ExternalIdentityLinks_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ExternalIdentityLinks_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinkRequests_ExpiresAt",
                table: "ExternalIdentityLinkRequests",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinkRequests_HouseholdId",
                table: "ExternalIdentityLinkRequests",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinkRequests_StateHash",
                table: "ExternalIdentityLinkRequests",
                column: "StateHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinkRequests_UserId",
                table: "ExternalIdentityLinkRequests",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinks_HouseholdId",
                table: "ExternalIdentityLinks",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinks_Issuer_Subject",
                table: "ExternalIdentityLinks",
                columns: new[] { "Issuer", "Subject" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ExternalIdentityLinks_UserId_Issuer",
                table: "ExternalIdentityLinks",
                columns: new[] { "UserId", "Issuer" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExternalIdentityLinkRequests");

            migrationBuilder.DropTable(
                name: "ExternalIdentityLinks");
        }
    }
}
