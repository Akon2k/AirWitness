using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Dashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class ExpandedRadioStation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Category",
                table: "RadioStations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "City",
                table: "RadioStations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DefaultMasterPath",
                table: "RadioStations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Frequency",
                table: "RadioStations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "RadioStations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Category",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "City",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "DefaultMasterPath",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "Frequency",
                table: "RadioStations");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "RadioStations");
        }
    }
}
