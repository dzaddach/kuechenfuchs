using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuechenfuchs.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalIdentityLinkTokensNotBefore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TokensNotBefore",
                table: "ExternalIdentityLinks",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            // Existing links must never accept tokens issued before they were created. Same rule as
            // KeycloakTokenValidation.CalculateTokensNotBefore with the default clock skew of 30 seconds.
            migrationBuilder.Sql(
                "UPDATE \"ExternalIdentityLinks\" SET \"TokensNotBefore\" = date_trunc('second', \"CreatedAt\") + interval '31 seconds';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TokensNotBefore",
                table: "ExternalIdentityLinks");
        }
    }
}
