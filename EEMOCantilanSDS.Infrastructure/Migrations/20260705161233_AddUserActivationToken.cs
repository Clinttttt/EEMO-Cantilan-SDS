using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserActivationToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ActivationTokenExpiry",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ActivationTokenHash",
                table: "Users",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ActivationTokenExpiry",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ActivationTokenHash",
                table: "Users");
        }
    }
}
