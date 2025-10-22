using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SundouleiaShared.Migrations
{
    /// <inheritdoc />
    public partial class _20251022RequestRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "from_world_id",
                table: "pair_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "from_zone_id",
                table: "pair_requests",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "from_world_id",
                table: "pair_requests");

            migrationBuilder.DropColumn(
                name: "from_zone_id",
                table: "pair_requests");
        }
    }
}
