using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Announcement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditingTimestamps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Announcements",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.Sql(
                """UPDATE "Announcements" SET "UpdatedAtUtc" = "CreatedAtUtc";""");

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "Collages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "Collages",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.Sql(
                """
                UPDATE "Collages" c
                SET "CreatedAtUtc" = a."CreatedAtUtc",
                    "UpdatedAtUtc" = a."CreatedAtUtc"
                FROM "Announcements" a
                WHERE c."AnnouncementId" = a."Id";
                """);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedAtUtc",
                table: "AspNetUsers",
                type: "timestamp with time zone",
                nullable: false,
                defaultValueSql: "CURRENT_TIMESTAMP");

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_UpdatedAtUtc",
                table: "Announcements",
                column: "UpdatedAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Announcements_UpdatedAtUtc",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "Collages");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Collages");

            migrationBuilder.DropColumn(
                name: "CreatedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "UpdatedAtUtc",
                table: "Announcements");
        }
    }
}
