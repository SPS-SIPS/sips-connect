using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Phase5SpecCompliance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "pacs002role",
                table: "isomessages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "returndedupkey",
                table: "isomessages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_iso_msg_txid_anchor",
                table: "isomessages",
                column: "txid");

            migrationBuilder.CreateIndex(
                name: "ux_iso_msg_type_rtr_dedup",
                table: "isomessages",
                columns: new[] { "messagetype", "returndedupkey" },
                unique: true,
                filter: "\"returndedupkey\" IS NOT NULL AND \"returndedupkey\" <> ''");

            migrationBuilder.CreateIndex(
                name: "ux_iso_msg_type_rtrid",
                table: "isomessages",
                columns: new[] { "messagetype", "returnid" },
                unique: true,
                filter: "\"returnid\" IS NOT NULL AND \"returnid\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_iso_msg_txid_anchor",
                table: "isomessages");

            migrationBuilder.DropIndex(
                name: "ux_iso_msg_type_rtr_dedup",
                table: "isomessages");

            migrationBuilder.DropIndex(
                name: "ux_iso_msg_type_rtrid",
                table: "isomessages");

            migrationBuilder.DropColumn(
                name: "pacs002role",
                table: "isomessages");

            migrationBuilder.DropColumn(
                name: "returndedupkey",
                table: "isomessages");
        }
    }
}
