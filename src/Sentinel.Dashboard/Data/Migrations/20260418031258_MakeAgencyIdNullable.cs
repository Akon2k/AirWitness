using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sentinel.Dashboard.Data.Migrations
{
    /// <inheritdoc />
    public partial class MakeAgencyIdNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MasterAudios_Agencies_AgencyId",
                table: "MasterAudios");

            migrationBuilder.AlterColumn<int>(
                name: "AgencyId",
                table: "MasterAudios",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_MasterAudios_Agencies_AgencyId",
                table: "MasterAudios",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MasterAudios_Agencies_AgencyId",
                table: "MasterAudios");

            migrationBuilder.AlterColumn<int>(
                name: "AgencyId",
                table: "MasterAudios",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_MasterAudios_Agencies_AgencyId",
                table: "MasterAudios",
                column: "AgencyId",
                principalTable: "Agencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
