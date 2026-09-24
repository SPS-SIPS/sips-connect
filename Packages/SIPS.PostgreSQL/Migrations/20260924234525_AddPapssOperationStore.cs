using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddPapssOperationStore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "papss_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    direction = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    operation = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    requestmessageid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    verificationid = table.Column<string>(type: "text", nullable: true),
                    endtoendid = table.Column<string>(type: "text", nullable: true),
                    txid = table.Column<string>(type: "text", nullable: true),
                    counterpartybic = table.Column<string>(type: "text", nullable: true),
                    accountid = table.Column<string>(type: "text", nullable: true),
                    accounttype = table.Column<string>(type: "text", nullable: true),
                    gatewaystate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    papssoutcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    bankdeliverystate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    verified = table.Column<bool>(type: "boolean", nullable: true),
                    accountname = table.Column<string>(type: "text", nullable: true),
                    currency = table.Column<string>(type: "text", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: true),
                    additionalinfo = table.Column<string>(type: "text", nullable: true),
                    admissioncode = table.Column<string>(type: "text", nullable: true),
                    reasoncode = table.Column<string>(type: "text", nullable: true),
                    signedrequest = table.Column<byte[]>(type: "bytea", nullable: true),
                    signedresponse = table.Column<byte[]>(type: "bytea", nullable: true),
                    sourcecreatedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    receivedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    createdat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deadlineat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_papss_operations", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "papss_operation_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    operationid = table.Column<Guid>(type: "uuid", nullable: true),
                    eventtype = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    messagetype = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    sourcemessageid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    rawxml = table.Column<byte[]>(type: "bytea", nullable: false),
                    receivedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pushstate = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    pushattempts = table.Column<int>(type: "integer", nullable: false),
                    pushnextattemptat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    pushlasterror = table.Column<string>(type: "text", nullable: true),
                    pushdeliveredat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_papss_operation_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_papss_operation_events_papss_operations_operationid",
                        column: x => x.operationid,
                        principalTable: "papss_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "papss_outbound_responses",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    operationid = table.Column<Guid>(type: "uuid", nullable: false),
                    bizmsgidr = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    messagetype = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    signedxml = table.Column<byte[]>(type: "bytea", nullable: false),
                    state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    nextattemptat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lasterror = table.Column<string>(type: "text", nullable: true),
                    admissioncode = table.Column<string>(type: "text", nullable: true),
                    submittedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    admittedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    createdat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updatedat = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_papss_outbound_responses", x => x.id);
                    table.ForeignKey(
                        name: "fk_papss_outbound_responses_papss_operations_operationid",
                        column: x => x.operationid,
                        principalTable: "papss_operations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_papss_event_operation",
                table: "papss_operation_events",
                column: "operationid");

            migrationBuilder.CreateIndex(
                name: "ix_papss_event_push_due",
                table: "papss_operation_events",
                columns: new[] { "pushstate", "pushnextattemptat" });

            migrationBuilder.CreateIndex(
                name: "ux_papss_event_type_source_msg",
                table: "papss_operation_events",
                columns: new[] { "eventtype", "sourcemessageid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_papss_op_completed_at",
                table: "papss_operations",
                column: "completedat");

            migrationBuilder.CreateIndex(
                name: "ix_papss_op_verification_id",
                table: "papss_operations",
                column: "verificationid");

            migrationBuilder.CreateIndex(
                name: "ux_papss_op_direction_request_msg",
                table: "papss_operations",
                columns: new[] { "direction", "requestmessageid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_papss_response_due",
                table: "papss_outbound_responses",
                columns: new[] { "state", "nextattemptat" });

            migrationBuilder.CreateIndex(
                name: "ux_papss_response_biz_msg_idr",
                table: "papss_outbound_responses",
                column: "bizmsgidr",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_papss_response_operation",
                table: "papss_outbound_responses",
                column: "operationid",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "papss_operation_events");

            migrationBuilder.DropTable(
                name: "papss_outbound_responses");

            migrationBuilder.DropTable(
                name: "papss_operations");
        }
    }
}
