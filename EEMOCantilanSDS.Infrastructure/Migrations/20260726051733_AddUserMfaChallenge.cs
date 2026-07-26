using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserMfaChallenge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "MfaChallengeTokenExpiry",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MfaChallengeTokenHash",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MfaChallengeTokenExpiry",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "MfaChallengeTokenHash",
                table: "Users");
        }
    }
}
