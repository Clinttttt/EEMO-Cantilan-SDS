using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EEMOCantilanSDS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddUserPasswordResetToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetRequestedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PasswordResetTokenExpiry",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasswordResetTokenHash",
                table: "Users",
                type: "text",
                nullable: true);

            // Backfill: existing LGU Heads (SuperAdmin) keep a working recovery path.
            // Rationale — a Head's address is already proven in practice: an onboarded Head only obtains
            // their password by clicking the one-time link emailed to that address, and a first-run setup
            // Head typed their own address. Heads are also the ONLY accounts with no one above them to
            // reset their password, so they are exactly who self-service recovery exists for.
            // Deliberately NOT backfilled for ordinary admins/collectors: their address was typed by the
            // Head and never confirmed, and they already have a recovery path (the Head resets them).
            // They become eligible once they complete activation, which now sets EmailVerified.
            migrationBuilder.Sql(@"
                UPDATE ""Users""
                   SET ""EmailVerified"" = TRUE
                 WHERE ""UserType"" = 'Admin'
                   AND ""Role"" = 1
                   AND ""IsActive"" = TRUE
                   AND ""IsDeleted"" = FALSE
                   AND ""Email"" IS NOT NULL
                   AND btrim(""Email"") <> '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailVerified",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetRequestedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenExpiry",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "PasswordResetTokenHash",
                table: "Users");
        }
    }
}
