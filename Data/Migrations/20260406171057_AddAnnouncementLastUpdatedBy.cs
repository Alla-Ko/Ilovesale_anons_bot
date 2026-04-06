using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Announcement.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAnnouncementLastUpdatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastUpdatedById",
                table: "Announcements",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Announcements_LastUpdatedById",
                table: "Announcements",
                column: "LastUpdatedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Announcements_AspNetUsers_LastUpdatedById",
                table: "Announcements",
                column: "LastUpdatedById",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql(
                """UPDATE "Announcements" SET "LastUpdatedById" = "CreatorId" WHERE "LastUpdatedById" IS NULL;""");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Announcements_AspNetUsers_LastUpdatedById",
                table: "Announcements");

            migrationBuilder.DropIndex(
                name: "IX_Announcements_LastUpdatedById",
                table: "Announcements");

            migrationBuilder.DropColumn(
                name: "LastUpdatedById",
                table: "Announcements");
        }
    }
}
