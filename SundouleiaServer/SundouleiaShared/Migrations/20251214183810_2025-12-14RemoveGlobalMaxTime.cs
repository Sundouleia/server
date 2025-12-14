using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SundouleiaShared.Migrations
{
    /// <inheritdoc />
    public partial class _20251214RemoveGlobalMaxTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "default_max_moodle_time",
                table: "user_global_permissions");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "default_max_moodle_time",
                table: "user_global_permissions",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));
        }
    }
}
