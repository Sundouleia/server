using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SundouleiaShared.Migrations
{
    /// <inheritdoc />
    public partial class _20260303RefactorForLoci : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "is_disabled",
                table: "user_profile");

            migrationBuilder.RenameColumn(
                name: "default_share_own_moodles",
                table: "user_global_permissions",
                newName: "default_share_own_loci_data");

            migrationBuilder.RenameColumn(
                name: "default_moodle_access",
                table: "user_global_permissions",
                newName: "default_loci_access");

            migrationBuilder.RenameColumn(
                name: "default_max_moodle_time",
                table: "user_global_permissions",
                newName: "default_max_loci_time");

            migrationBuilder.RenameColumn(
                name: "share_own_moodles",
                table: "client_pair_permissions",
                newName: "share_own_loci_data");

            migrationBuilder.RenameColumn(
                name: "moodle_access",
                table: "client_pair_permissions",
                newName: "loci_access");

            migrationBuilder.RenameColumn(
                name: "max_moodle_time",
                table: "client_pair_permissions",
                newName: "max_loci_time");

            migrationBuilder.AddColumn<int>(
                name: "earned_achievements",
                table: "user_profile",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "title_id",
                table: "user_profile",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "false_report_strikes",
                table: "account_reputation",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "earned_achievements",
                table: "user_profile");

            migrationBuilder.DropColumn(
                name: "title_id",
                table: "user_profile");

            migrationBuilder.DropColumn(
                name: "false_report_strikes",
                table: "account_reputation");

            migrationBuilder.RenameColumn(
                name: "default_share_own_loci_data",
                table: "user_global_permissions",
                newName: "default_share_own_moodles");

            migrationBuilder.RenameColumn(
                name: "default_max_loci_time",
                table: "user_global_permissions",
                newName: "default_max_moodle_time");

            migrationBuilder.RenameColumn(
                name: "default_loci_access",
                table: "user_global_permissions",
                newName: "default_moodle_access");

            migrationBuilder.RenameColumn(
                name: "share_own_loci_data",
                table: "client_pair_permissions",
                newName: "share_own_moodles");

            migrationBuilder.RenameColumn(
                name: "max_loci_time",
                table: "client_pair_permissions",
                newName: "max_moodle_time");

            migrationBuilder.RenameColumn(
                name: "loci_access",
                table: "client_pair_permissions",
                newName: "moodle_access");

            migrationBuilder.AddColumn<bool>(
                name: "is_disabled",
                table: "user_profile",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
