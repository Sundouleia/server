using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SundouleiaShared.Migrations
{
    /// <inheritdoc />
    public partial class _20251002 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "banned_registrations",
                columns: table => new
                {
                    discord_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_banned_registrations", x => x.discord_id);
                });

            migrationBuilder.CreateTable(
                name: "banned_users",
                columns: table => new
                {
                    character_identification = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    user_uid = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_banned_users", x => x.character_identification);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    uid = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    alias = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    last_login = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tier = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.uid);
                });

            migrationBuilder.CreateTable(
                name: "account_claim_auth",
                columns: table => new
                {
                    discord_id = table.Column<decimal>(type: "numeric(20,0)", nullable: false),
                    initial_generated_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    verification_code = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    user_uid = table.Column<string>(type: "character varying(10)", nullable: true),
                    started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_claim_auth", x => x.discord_id);
                    table.ForeignKey(
                        name: "fk_account_claim_auth_users_user_uid",
                        column: x => x.user_uid,
                        principalTable: "users",
                        principalColumn: "uid");
                });

            migrationBuilder.CreateTable(
                name: "account_reputation",
                columns: table => new
                {
                    user_uid = table.Column<string>(type: "character varying(10)", nullable: false),
                    is_verified = table.Column<bool>(type: "boolean", nullable: false),
                    is_banned = table.Column<bool>(type: "boolean", nullable: false),
                    profile_viewing = table.Column<bool>(type: "boolean", nullable: false),
                    profile_view_timeout = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    profile_view_strikes = table.Column<int>(type: "integer", nullable: false),
                    profile_editing = table.Column<bool>(type: "boolean", nullable: false),
                    profile_edit_timeout = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    profile_edit_strikes = table.Column<int>(type: "integer", nullable: false),
                    radar_usage = table.Column<bool>(type: "boolean", nullable: false),
                    radar_timeout = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    radar_strikes = table.Column<int>(type: "integer", nullable: false),
                    chat_usage = table.Column<bool>(type: "boolean", nullable: false),
                    chat_timeout = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    chat_strikes = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_account_reputation", x => x.user_uid);
                    table.ForeignKey(
                        name: "fk_account_reputation_users_user_uid",
                        column: x => x.user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "blocked_users",
                columns: table => new
                {
                    user_uid = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    other_user_uid = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_blocked_users", x => new { x.user_uid, x.other_user_uid });
                    table.ForeignKey(
                        name: "fk_blocked_users_users_other_user_uid",
                        column: x => x.other_user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_blocked_users_users_user_uid",
                        column: x => x.user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_pair_permissions",
                columns: table => new
                {
                    user_uid = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    other_user_uid = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    pause_visuals = table.Column<bool>(type: "boolean", nullable: false),
                    allow_animations = table.Column<bool>(type: "boolean", nullable: false),
                    allow_sounds = table.Column<bool>(type: "boolean", nullable: false),
                    allow_vfx = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_pair_permissions", x => new { x.user_uid, x.other_user_uid });
                    table.ForeignKey(
                        name: "fk_client_pair_permissions_users_other_user_uid",
                        column: x => x.other_user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_pair_permissions_users_user_uid",
                        column: x => x.user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "client_pairs",
                columns: table => new
                {
                    user_uid = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    other_user_uid = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    timestamp = table.Column<byte[]>(type: "bytea", rowVersion: true, nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_temporary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_client_pairs", x => new { x.user_uid, x.other_user_uid });
                    table.ForeignKey(
                        name: "fk_client_pairs_users_other_user_uid",
                        column: x => x.other_user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_client_pairs_users_user_uid",
                        column: x => x.user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "pair_requests",
                columns: table => new
                {
                    user_uid = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    other_user_uid = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    creation_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    is_temporary = table.Column<bool>(type: "boolean", nullable: false),
                    attached_message = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pair_requests", x => new { x.user_uid, x.other_user_uid });
                    table.ForeignKey(
                        name: "fk_pair_requests_users_other_user_uid",
                        column: x => x.other_user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_pair_requests_users_user_uid",
                        column: x => x.user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "reported_profiles",
                columns: table => new
                {
                    report_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    report_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    snapshot_is_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    snapshot_image = table.Column<string>(type: "text", nullable: false),
                    snapshot_description = table.Column<string>(type: "text", nullable: false),
                    reporting_user_uid = table.Column<string>(type: "character varying(10)", nullable: true),
                    reported_user_uid = table.Column<string>(type: "character varying(10)", nullable: true),
                    report_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reported_profiles", x => x.report_id);
                    table.ForeignKey(
                        name: "fk_reported_profiles_users_reported_user_uid",
                        column: x => x.reported_user_uid,
                        principalTable: "users",
                        principalColumn: "uid");
                    table.ForeignKey(
                        name: "fk_reported_profiles_users_reporting_user_uid",
                        column: x => x.reporting_user_uid,
                        principalTable: "users",
                        principalColumn: "uid");
                });

            migrationBuilder.CreateTable(
                name: "reported_radars",
                columns: table => new
                {
                    report_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    report_time = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    world_id = table.Column<int>(type: "integer", nullable: false),
                    territory_id = table.Column<int>(type: "integer", nullable: false),
                    recent_radar_chat_history = table.Column<string>(type: "text", nullable: true),
                    reported_user_uid = table.Column<string>(type: "text", nullable: true),
                    is_indoor = table.Column<bool>(type: "boolean", nullable: false),
                    apartment_division = table.Column<byte>(type: "smallint", nullable: false),
                    plot_index = table.Column<byte>(type: "smallint", nullable: false),
                    ward_index = table.Column<byte>(type: "smallint", nullable: false),
                    room_number = table.Column<byte>(type: "smallint", nullable: false),
                    reporter_uid = table.Column<string>(type: "character varying(10)", nullable: true),
                    report_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reported_radars", x => x.report_id);
                    table.ForeignKey(
                        name: "fk_reported_radars_users_reporter_uid",
                        column: x => x.reporter_uid,
                        principalTable: "users",
                        principalColumn: "uid");
                });

            migrationBuilder.CreateTable(
                name: "user_global_permissions",
                columns: table => new
                {
                    user_uid = table.Column<string>(type: "character varying(10)", nullable: false),
                    default_allow_animations = table.Column<bool>(type: "boolean", nullable: false),
                    default_allow_sounds = table.Column<bool>(type: "boolean", nullable: false),
                    default_allow_vfx = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_global_permissions", x => x.user_uid);
                    table.ForeignKey(
                        name: "fk_user_global_permissions_users_user_uid",
                        column: x => x.user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_profile",
                columns: table => new
                {
                    user_uid = table.Column<string>(type: "character varying(10)", nullable: false),
                    is_public = table.Column<bool>(type: "boolean", nullable: false),
                    is_nsfw = table.Column<bool>(type: "boolean", nullable: false),
                    avatar_vis = table.Column<int>(type: "integer", nullable: false),
                    description_vis = table.Column<int>(type: "integer", nullable: false),
                    decoration_vis = table.Column<int>(type: "integer", nullable: false),
                    flagged_for_report = table.Column<bool>(type: "boolean", nullable: false),
                    is_disabled = table.Column<bool>(type: "boolean", nullable: false),
                    base64avatar_data = table.Column<string>(type: "text", nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    main_bg = table.Column<int>(type: "integer", nullable: false),
                    main_border = table.Column<int>(type: "integer", nullable: false),
                    avatar_bg = table.Column<int>(type: "integer", nullable: false),
                    avatar_border = table.Column<int>(type: "integer", nullable: false),
                    avatar_overlay = table.Column<int>(type: "integer", nullable: false),
                    description_bg = table.Column<int>(type: "integer", nullable: false),
                    description_border = table.Column<int>(type: "integer", nullable: false),
                    description_overlay = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_profile", x => x.user_uid);
                    table.ForeignKey(
                        name: "fk_user_profile_users_user_uid",
                        column: x => x.user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_radar_info",
                columns: table => new
                {
                    user_uid = table.Column<string>(type: "character varying(10)", nullable: false),
                    world_id = table.Column<int>(type: "integer", nullable: false),
                    territory_id = table.Column<int>(type: "integer", nullable: false),
                    hashed_cid = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_radar_info", x => x.user_uid);
                    table.ForeignKey(
                        name: "fk_user_radar_info_users_user_uid",
                        column: x => x.user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auth",
                columns: table => new
                {
                    hashed_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    user_uid = table.Column<string>(type: "character varying(10)", nullable: false),
                    primary_user_uid = table.Column<string>(type: "character varying(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auth", x => x.hashed_key);
                    table.ForeignKey(
                        name: "fk_auth_account_reputation_primary_user_uid",
                        column: x => x.primary_user_uid,
                        principalTable: "account_reputation",
                        principalColumn: "user_uid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_auth_users_primary_user_uid",
                        column: x => x.primary_user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_auth_users_user_uid",
                        column: x => x.user_uid,
                        principalTable: "users",
                        principalColumn: "uid",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_account_claim_auth_user_uid",
                table: "account_claim_auth",
                column: "user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_auth_primary_user_uid",
                table: "auth",
                column: "primary_user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_auth_user_uid",
                table: "auth",
                column: "user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_blocked_users_other_user_uid",
                table: "blocked_users",
                column: "other_user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_blocked_users_user_uid",
                table: "blocked_users",
                column: "user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_client_pair_permissions_other_user_uid",
                table: "client_pair_permissions",
                column: "other_user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_client_pair_permissions_user_uid",
                table: "client_pair_permissions",
                column: "user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_client_pairs_is_temporary",
                table: "client_pairs",
                column: "is_temporary");

            migrationBuilder.CreateIndex(
                name: "ix_client_pairs_other_user_uid",
                table: "client_pairs",
                column: "other_user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_client_pairs_user_uid",
                table: "client_pairs",
                column: "user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_pair_requests_other_user_uid",
                table: "pair_requests",
                column: "other_user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_pair_requests_user_uid",
                table: "pair_requests",
                column: "user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_reported_profiles_reported_user_uid",
                table: "reported_profiles",
                column: "reported_user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_reported_profiles_reporting_user_uid",
                table: "reported_profiles",
                column: "reporting_user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_reported_radars_kind",
                table: "reported_radars",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "ix_reported_radars_reporter_uid",
                table: "reported_radars",
                column: "reporter_uid");

            migrationBuilder.CreateIndex(
                name: "ix_reported_radars_territory_id",
                table: "reported_radars",
                column: "territory_id");

            migrationBuilder.CreateIndex(
                name: "ix_reported_radars_world_id",
                table: "reported_radars",
                column: "world_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_global_permissions_user_uid",
                table: "user_global_permissions",
                column: "user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_user_profile_user_uid",
                table: "user_profile",
                column: "user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_user_radar_info_territory_id",
                table: "user_radar_info",
                column: "territory_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_radar_info_user_uid",
                table: "user_radar_info",
                column: "user_uid");

            migrationBuilder.CreateIndex(
                name: "ix_user_radar_info_world_id",
                table: "user_radar_info",
                column: "world_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "account_claim_auth");

            migrationBuilder.DropTable(
                name: "auth");

            migrationBuilder.DropTable(
                name: "banned_registrations");

            migrationBuilder.DropTable(
                name: "banned_users");

            migrationBuilder.DropTable(
                name: "blocked_users");

            migrationBuilder.DropTable(
                name: "client_pair_permissions");

            migrationBuilder.DropTable(
                name: "client_pairs");

            migrationBuilder.DropTable(
                name: "pair_requests");

            migrationBuilder.DropTable(
                name: "reported_profiles");

            migrationBuilder.DropTable(
                name: "reported_radars");

            migrationBuilder.DropTable(
                name: "user_global_permissions");

            migrationBuilder.DropTable(
                name: "user_profile");

            migrationBuilder.DropTable(
                name: "user_radar_info");

            migrationBuilder.DropTable(
                name: "account_reputation");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
