using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddMsgIdUniqueConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DELETE FROM isomessages
                WHERE id NOT IN (
                    SELECT MAX(id)
                    FROM isomessages
                    WHERE msgid IS NOT NULL AND msgid <> ''
                    GROUP BY messagetype, msgid
                )
                AND msgid IS NOT NULL AND msgid <> '';
            ");

            migrationBuilder.Sql(@"
                DELETE FROM isomessages
                WHERE id NOT IN (
                    SELECT MAX(id)
                    FROM isomessages
                    WHERE txid IS NOT NULL AND txid <> ''
                    GROUP BY messagetype, txid
                )
                AND txid IS NOT NULL AND txid <> '';
            ");

            migrationBuilder.CreateIndex(
                name: "ux_iso_msg_type_msgid",
                table: "isomessages",
                columns: new[] { "messagetype", "msgid" },
                unique: true,
                filter: "\"msgid\" IS NOT NULL AND \"msgid\" <> ''");

            migrationBuilder.CreateIndex(
                name: "ux_iso_msg_type_txid",
                table: "isomessages",
                columns: new[] { "messagetype", "txid" },
                unique: true,
                filter: "\"txid\" IS NOT NULL AND \"txid\" <> ''");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_iso_msg_type_msgid",
                table: "isomessages");

            migrationBuilder.DropIndex(
                name: "ux_iso_msg_type_txid",
                table: "isomessages");

            migrationBuilder.CreateIndex(
                name: "ux_iso_msg_type_msgid",
                table: "isomessages",
                columns: new[] { "messagetype", "msgid" },
                unique: true,
                filter: "\"MsgId\" IS NOT NULL AND \"MsgId\" <> ''");

            migrationBuilder.CreateIndex(
                name: "ux_iso_msg_type_txid",
                table: "isomessages",
                columns: new[] { "messagetype", "txid" },
                unique: true,
                filter: "\"TxId\" IS NOT NULL AND \"TxId\" <> ''");
        }
    }
}
