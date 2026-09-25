using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddPapssPaymentOperations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "amount",
                table: "papss_operations",
                type: "numeric(18,5)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "isomessageid",
                table: "papss_operations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "localinstrument",
                table: "papss_operations",
                type: "character varying(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "msgid",
                table: "papss_operations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "originalendtoendid",
                table: "papss_operations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "originaloperationid",
                table: "papss_operations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "originaltxid",
                table: "papss_operations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "paymentstatus",
                table: "papss_operations",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "requestfingerprint",
                table: "papss_operations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "returnid",
                table: "papss_operations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "statusat",
                table: "papss_operations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "statusconflict",
                table: "papss_operations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "statusreasoncode",
                table: "papss_operations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "amount",
                table: "papss_operation_events",
                type: "numeric(18,5)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "correlation",
                table: "papss_operation_events",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "currency",
                table: "papss_operation_events",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "disposition",
                table: "papss_operation_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "note",
                table: "papss_operation_events",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "originalendtoendid",
                table: "papss_operation_events",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "originalmessageid",
                table: "papss_operation_events",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "originalmessagetype",
                table: "papss_operation_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "originaltxid",
                table: "papss_operation_events",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reasoncode",
                table: "papss_operation_events",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "status",
                table: "papss_operation_events",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_papss_op_end_to_end_id",
                table: "papss_operations",
                column: "endtoendid");

            migrationBuilder.CreateIndex(
                name: "ix_papss_op_iso_message",
                table: "papss_operations",
                column: "isomessageid");

            migrationBuilder.CreateIndex(
                name: "ix_papss_op_msg_id",
                table: "papss_operations",
                column: "msgid");

            migrationBuilder.CreateIndex(
                name: "ix_papss_op_original",
                table: "papss_operations",
                column: "originaloperationid");

            migrationBuilder.CreateIndex(
                name: "ix_papss_op_tx_id",
                table: "papss_operations",
                column: "txid");

            migrationBuilder.CreateIndex(
                name: "ux_papss_op_payment_txid",
                table: "papss_operations",
                columns: new[] { "direction", "operation", "txid" },
                unique: true,
                filter: "operation = 'PAYMENT' AND txid IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_papss_op_return_id",
                table: "papss_operations",
                columns: new[] { "direction", "returnid" },
                unique: true,
                filter: "operation = 'RETURN' AND returnid IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_papss_event_type_disposition",
                table: "papss_operation_events",
                columns: new[] { "eventtype", "disposition" });

            migrationBuilder.AddForeignKey(
                name: "fk_papss_operations_papss_operations_originaloperationid",
                table: "papss_operations",
                column: "originaloperationid",
                principalTable: "papss_operations",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_papss_operations_papss_operations_originaloperationid",
                table: "papss_operations");

            migrationBuilder.DropIndex(
                name: "ix_papss_op_end_to_end_id",
                table: "papss_operations");

            migrationBuilder.DropIndex(
                name: "ix_papss_op_iso_message",
                table: "papss_operations");

            migrationBuilder.DropIndex(
                name: "ix_papss_op_msg_id",
                table: "papss_operations");

            migrationBuilder.DropIndex(
                name: "ix_papss_op_original",
                table: "papss_operations");

            migrationBuilder.DropIndex(
                name: "ix_papss_op_tx_id",
                table: "papss_operations");

            migrationBuilder.DropIndex(
                name: "ux_papss_op_payment_txid",
                table: "papss_operations");

            migrationBuilder.DropIndex(
                name: "ux_papss_op_return_id",
                table: "papss_operations");

            migrationBuilder.DropIndex(
                name: "ix_papss_event_type_disposition",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "amount",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "isomessageid",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "localinstrument",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "msgid",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "originalendtoendid",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "originaloperationid",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "originaltxid",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "paymentstatus",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "requestfingerprint",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "returnid",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "statusat",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "statusconflict",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "statusreasoncode",
                table: "papss_operations");

            migrationBuilder.DropColumn(
                name: "amount",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "correlation",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "currency",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "disposition",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "note",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "originalendtoendid",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "originalmessageid",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "originalmessagetype",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "originaltxid",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "reasoncode",
                table: "papss_operation_events");

            migrationBuilder.DropColumn(
                name: "status",
                table: "papss_operation_events");
        }
    }
}
