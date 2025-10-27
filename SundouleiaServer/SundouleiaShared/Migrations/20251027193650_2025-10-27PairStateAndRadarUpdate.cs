using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SundouleiaShared.Migrations
{
    /// <inheritdoc />
    public partial class _20251027PairStateAndRadarUpdate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_client_pairs_is_temporary",
                table: "client_pairs");

            migrationBuilder.DropColumn(
                name: "is_temporary",
                table: "client_pairs");

            migrationBuilder.AddColumn<string>(
                name: "temp_accepter_uid",
                table: "client_pairs",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_client_pairs_temp_accepter_uid",
                table: "client_pairs",
                column: "temp_accepter_uid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_client_pairs_temp_accepter_uid",
                table: "client_pairs");

            migrationBuilder.DropColumn(
                name: "temp_accepter_uid",
                table: "client_pairs");

            migrationBuilder.AddColumn<bool>(
                name: "is_temporary",
                table: "client_pairs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "ix_client_pairs_is_temporary",
                table: "client_pairs",
                column: "is_temporary");
        }
    }
}
