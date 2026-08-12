using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class SponsorPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "sponsor_ghost_skin",
                table: "preference",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sponsor_looc_color",
                table: "preference",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "sponsor_ooc_color",
                table: "preference",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "sponsor_ghost_skin",
                table: "preference");

            migrationBuilder.DropColumn(
                name: "sponsor_looc_color",
                table: "preference");

            migrationBuilder.DropColumn(
                name: "sponsor_ooc_color",
                table: "preference");
        }
    }
}
