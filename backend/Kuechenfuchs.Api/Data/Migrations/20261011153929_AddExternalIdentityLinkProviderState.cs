using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuechenfuchs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalIdentityLinkProviderState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastBearerAccessAt",
                table: "ExternalIdentityLinks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ProviderSessionsRevokedAt",
                table: "ExternalIdentityLinks",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastBearerAccessAt",
                table: "ExternalIdentityLinks");

            migrationBuilder.DropColumn(
                name: "ProviderSessionsRevokedAt",
                table: "ExternalIdentityLinks");
        }
    }
}
