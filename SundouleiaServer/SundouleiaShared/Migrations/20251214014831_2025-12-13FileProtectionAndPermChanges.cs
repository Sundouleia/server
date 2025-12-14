using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SundouleiaShared.Migrations
{
    /// <inheritdoc />
    public partial class _20251213FileProtectionAndPermChanges : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<TimeSpan>(
                name: "default_max_moodle_time",
                table: "user_global_permissions",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<short>(
                name: "default_moodle_access",
                table: "user_global_permissions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "share_own_moodles",
                table: "user_global_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "preferred_nickname",
                table: "pair_requests",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "max_moodle_time",
                table: "client_pair_permissions",
                type: "interval",
                nullable: false,
                defaultValue: new TimeSpan(0, 0, 0, 0, 0));

            migrationBuilder.AddColumn<short>(
                name: "moodle_access",
                table: "client_pair_permissions",
                type: "smallint",
                nullable: false,
                defaultValue: (short)0);

            migrationBuilder.AddColumn<bool>(
                name: "share_own_moodles",
                table: "client_pair_permissions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "protected_sma_files",
                columns: table => new
                {
                    owner_uid = table.Column<string>(type: "character varying(10)", nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    data_hash = table.Column<string>(type: "text", nullable: true),
                    encrypted_file_key = table.Column<string>(type: "text", nullable: true),
                    password = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    expire_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    allowed_hashes_csv = table.Column<string>(type: "text", nullable: true),
                    allowed_ui_ds_csv = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_protected_sma_files", x => new { x.owner_uid, x.file_id });
                    table.ForeignKey(
                        name: "fk_protected_sma_files_users_owner_uid",
                        column: x => x.owner_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_protected_sma_files_data_hash",
                table: "protected_sma_files",
                column: "data_hash");

            migrationBuilder.CreateIndex(
                name: "ix_protected_sma_files_file_id",
                table: "protected_sma_files",
                column: "file_id");

            migrationBuilder.CreateIndex(
                name: "ix_protected_sma_files_owner_uid",
                table: "protected_sma_files",
                column: "owner_uid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "protected_sma_files");

            migrationBuilder.DropColumn(
                name: "default_max_moodle_time",
                table: "user_global_permissions");

            migrationBuilder.DropColumn(
                name: "default_moodle_access",
                table: "user_global_permissions");

            migrationBuilder.DropColumn(
                name: "share_own_moodles",
                table: "user_global_permissions");

            migrationBuilder.DropColumn(
                name: "preferred_nickname",
                table: "pair_requests");

            migrationBuilder.DropColumn(
                name: "max_moodle_time",
                table: "client_pair_permissions");

            migrationBuilder.DropColumn(
                name: "moodle_access",
                table: "client_pair_permissions");

            migrationBuilder.DropColumn(
                name: "share_own_moodles",
                table: "client_pair_permissions");
        }
    }
}
