using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ComicPlatform.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,");

            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    target_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    target_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    translation_house_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ip = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    data = table.Column<string>(type: "jsonb", nullable: true),
                    success = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "storage_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_storage_tasks", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "system_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "tags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    slug = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tags", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "translation_houses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    logo_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    suspended_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                    links = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_houses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    normalized_username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    normalized_email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    display_name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    system_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    must_change_password = table.Column<bool>(type: "boolean", nullable: false),
                    failed_login_count = table.Column<int>(type: "integer", nullable: false),
                    lockout_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_login_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "comics",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    translation_house_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    translated_title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(5000)", maxLength: 5000, nullable: true),
                    author = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    artist = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    cover_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    is_mature = table.Column<bool>(type: "boolean", nullable: false),
                    search_key = table.Column<string>(type: "character varying(1200)", maxLength: 1200, nullable: false),
                    published_chapter_count = table.Column<int>(type: "integer", nullable: false),
                    last_chapter_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comics", x => x.id);
                    table.ForeignKey(
                        name: "fk_comics_translation_houses_translation_house_id",
                        column: x => x.translation_house_id,
                        principalTable: "translation_houses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "refresh_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    family_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoke_reason = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    replaced_by_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_refresh_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_refresh_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "translation_house_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    translation_house_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    permissions = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_translation_house_members", x => x.id);
                    table.ForeignKey(
                        name: "fk_translation_house_members_translation_houses_translation_ho",
                        column: x => x.translation_house_id,
                        principalTable: "translation_houses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_translation_house_members_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comic_tags",
                columns: table => new
                {
                    comic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tag_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comic_tags", x => new { x.comic_id, x.tag_id });
                    table.ForeignKey(
                        name: "fk_comic_tags_comics_comic_id",
                        column: x => x.comic_id,
                        principalTable: "comics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_comic_tags_tags_tag_id",
                        column: x => x.tag_id,
                        principalTable: "tags",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "comic_view_daily",
                columns: table => new
                {
                    comic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    views = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comic_view_daily", x => new { x.comic_id, x.day });
                    table.ForeignKey(
                        name: "fk_comic_view_daily_comics_comic_id",
                        column: x => x.comic_id,
                        principalTable: "comics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "follows",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_follows", x => new { x.user_id, x.comic_id });
                    table.ForeignKey(
                        name: "fk_follows_comics_comic_id",
                        column: x => x.comic_id,
                        principalTable: "comics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_follows_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapter_packages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    format_version = table.Column<int>(type: "integer", nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    page_count = table.Column<int>(type: "integer", nullable: false),
                    checksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chapter_packages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "chapter_pages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page_number = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    file_size = table.Column<long>(type: "bigint", nullable: false),
                    storage_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chapter_pages", x => x.id);
                    table.ForeignKey(
                        name: "fk_chapter_pages_chapter_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "chapter_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    comic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_number = table.Column<decimal>(type: "numeric(8,2)", precision: 8, scale: 2, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    published_package_id = table.Column<Guid>(type: "uuid", nullable: true),
                    latest_package_version = table.Column<int>(type: "integer", nullable: false),
                    page_count = table.Column<int>(type: "integer", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chapters", x => x.id);
                    table.ForeignKey(
                        name: "fk_chapters_chapter_packages_published_package_id",
                        column: x => x.published_package_id,
                        principalTable: "chapter_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_chapters_comics_comic_id",
                        column: x => x.comic_id,
                        principalTable: "comics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "processing_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    package_id = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    stage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    progress_done = table.Column<int>(type: "integer", nullable: false),
                    progress_total = table.Column<int>(type: "integer", nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    error_message = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    upload_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    upload_size = table.Column<long>(type: "bigint", nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_processing_jobs_chapter_packages_package_id",
                        column: x => x.package_id,
                        principalTable: "chapter_packages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_processing_jobs_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_processing_jobs_users_requested_by_user_id",
                        column: x => x.requested_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reading_progress",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comic_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chapter_id = table.Column<Guid>(type: "uuid", nullable: false),
                    page = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reading_progress", x => new { x.user_id, x.comic_id });
                    table.ForeignKey(
                        name: "fk_reading_progress_chapters_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapters",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reading_progress_comics_comic_id",
                        column: x => x.comic_id,
                        principalTable: "comics",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reading_progress_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_action",
                table: "audit_logs",
                column: "action");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_user_id_at",
                table: "audit_logs",
                columns: new[] { "actor_user_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_at",
                table: "audit_logs",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_translation_house_id_at",
                table: "audit_logs",
                columns: new[] { "translation_house_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_chapter_packages_chapter_id_version",
                table: "chapter_packages",
                columns: new[] { "chapter_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chapter_packages_status",
                table: "chapter_packages",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_pages_chapter_id",
                table: "chapter_pages",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_pages_package_id_page_number",
                table: "chapter_pages",
                columns: new[] { "package_id", "page_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chapters_comic_id_chapter_number",
                table: "chapters",
                columns: new[] { "comic_id", "chapter_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chapters_comic_id_status",
                table: "chapters",
                columns: new[] { "comic_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_chapters_published_package_id",
                table: "chapters",
                column: "published_package_id");

            migrationBuilder.CreateIndex(
                name: "ix_comic_tags_tag_id",
                table: "comic_tags",
                column: "tag_id");

            migrationBuilder.CreateIndex(
                name: "ix_comic_view_daily_day",
                table: "comic_view_daily",
                column: "day");

            migrationBuilder.CreateIndex(
                name: "ix_comics_last_chapter_published_at",
                table: "comics",
                column: "last_chapter_published_at");

            migrationBuilder.CreateIndex(
                name: "ix_comics_search_key",
                table: "comics",
                column: "search_key")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_comics_translation_house_id_slug",
                table: "comics",
                columns: new[] { "translation_house_id", "slug" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_comics_translation_house_id_updated_at",
                table: "comics",
                columns: new[] { "translation_house_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_follows_comic_id",
                table: "follows",
                column: "comic_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_chapter_id",
                table: "processing_jobs",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_package_id",
                table: "processing_jobs",
                column: "package_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_requested_by_user_id",
                table: "processing_jobs",
                column: "requested_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_jobs_status_created_at",
                table: "processing_jobs",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reading_progress_chapter_id",
                table: "reading_progress",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "ix_reading_progress_comic_id",
                table: "reading_progress",
                column: "comic_id");

            migrationBuilder.CreateIndex(
                name: "ix_reading_progress_user_id_updated_at",
                table: "reading_progress",
                columns: new[] { "user_id", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_family_id",
                table: "refresh_tokens",
                column: "family_id");

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_token_hash",
                table: "refresh_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_refresh_tokens_user_id",
                table: "refresh_tokens",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_storage_tasks_next_attempt_at",
                table: "storage_tasks",
                column: "next_attempt_at");

            migrationBuilder.CreateIndex(
                name: "ix_tags_name",
                table: "tags",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tags_slug",
                table: "tags",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_house_members_translation_house_id_user_id",
                table: "translation_house_members",
                columns: new[] { "translation_house_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_house_members_user_id",
                table: "translation_house_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_translation_houses_name",
                table: "translation_houses",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_translation_houses_slug",
                table: "translation_houses",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_username",
                table: "users",
                column: "normalized_username",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_chapter_packages_chapters_chapter_id",
                table: "chapter_packages",
                column: "chapter_id",
                principalTable: "chapters",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_chapter_packages_chapters_chapter_id",
                table: "chapter_packages");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "chapter_pages");

            migrationBuilder.DropTable(
                name: "comic_tags");

            migrationBuilder.DropTable(
                name: "comic_view_daily");

            migrationBuilder.DropTable(
                name: "follows");

            migrationBuilder.DropTable(
                name: "processing_jobs");

            migrationBuilder.DropTable(
                name: "reading_progress");

            migrationBuilder.DropTable(
                name: "refresh_tokens");

            migrationBuilder.DropTable(
                name: "storage_tasks");

            migrationBuilder.DropTable(
                name: "system_settings");

            migrationBuilder.DropTable(
                name: "translation_house_members");

            migrationBuilder.DropTable(
                name: "tags");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "chapters");

            migrationBuilder.DropTable(
                name: "chapter_packages");

            migrationBuilder.DropTable(
                name: "comics");

            migrationBuilder.DropTable(
                name: "translation_houses");
        }
    }
}
