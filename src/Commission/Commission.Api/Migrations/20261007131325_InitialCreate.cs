using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Commission.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commission_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    active_schema = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commission_settings", x => x.id);
                    table.CheckConstraint("ck_commission_settings_singleton", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => x.message_id);
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "profit_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    user_external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    profit = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_profit_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "commissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_external_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(28,8)", precision: 28, scale: 8, nullable: false),
                    schema_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    accrued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    paid_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    payout_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commissions", x => x.id);
                    table.ForeignKey(
                        name: "fk_commissions_profit_events_event_id",
                        column: x => x.event_id,
                        principalTable: "profit_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "commission_settings",
                columns: new[] { "id", "active_schema" },
                values: new object[] { 1, 1 });

            migrationBuilder.CreateIndex(
                name: "ix_commissions_event_id_partner_external_id",
                table: "commissions",
                columns: new[] { "event_id", "partner_external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_unpublished",
                table: "outbox_messages",
                column: "created_at",
                filter: "published_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_profit_events_external_id",
                table: "profit_events",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_profit_events_user_external_id_occurred_at",
                table: "profit_events",
                columns: new[] { "user_external_id", "occurred_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commission_settings");

            migrationBuilder.DropTable(
                name: "commissions");

            migrationBuilder.DropTable(
                name: "inbox_messages");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "profit_events");
        }
    }
}
