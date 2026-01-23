using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SundouleiaShared.Migrations
{
    /// <inheritdoc />
    public partial class _20260123PermissionPolishing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "share_own_moodles",
                table: "user_global_permissions",
                newName: "default_share_own_moodles");

            migrationBuilder.AddColumn<TimeSpan>(
                name: "default_max_moodle_time",
                table: "user_global_permissions",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_max_moodle_time",
                table: "user_global_permissions");

            migrationBuilder.RenameColumn(
                name: "default_share_own_moodles",
                table: "user_global_permissions",
                newName: "share_own_moodles");
        }
    }
}
