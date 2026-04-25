using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Dashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCascadeDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationLogs_RadioStations_RadioStationId",
                table: "NotificationLogs");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationLogs_RadioStations_RadioStationId",
                table: "NotificationLogs",
                column: "RadioStationId",
                principalTable: "RadioStations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_NotificationLogs_RadioStations_RadioStationId",
                table: "NotificationLogs");

            migrationBuilder.AddForeignKey(
                name: "FK_NotificationLogs_RadioStations_RadioStationId",
                table: "NotificationLogs",
                column: "RadioStationId",
                principalTable: "RadioStations",
                principalColumn: "Id");
        }
    }
}
