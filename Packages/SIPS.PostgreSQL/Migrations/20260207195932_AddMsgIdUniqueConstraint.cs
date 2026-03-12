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
                -- 1. Identify rows to delete due to MsgId duplicates
                WITH duplicates_msgid AS (
                    SELECT id 
                    FROM isomessages 
                    WHERE id NOT IN (
                        SELECT MAX(id)
                        FROM isomessages
                        WHERE msgid IS NOT NULL AND msgid <> ''
                        GROUP BY messagetype, msgid
                    )
                    AND msgid IS NOT NULL AND msgid <> ''
                )
                DELETE FROM transactions WHERE isomessageid IN (SELECT id FROM duplicates_msgid);
                
                WITH duplicates_msgid AS (
                    SELECT id 
                    FROM isomessages 
                    WHERE id NOT IN (
                        SELECT MAX(id)
                        FROM isomessages
                        WHERE msgid IS NOT NULL AND msgid <> ''
                        GROUP BY messagetype, msgid
                    )
                    AND msgid IS NOT NULL AND msgid <> ''
                )
                DELETE FROM isomessagestatuses WHERE isomessageid IN (SELECT id FROM duplicates_msgid);

                WITH duplicates_msgid AS (
                    SELECT id 
                    FROM isomessages 
                    WHERE id NOT IN (
                        SELECT MAX(id)
                        FROM isomessages
                        WHERE msgid IS NOT NULL AND msgid <> ''
                        GROUP BY messagetype, msgid
                    )
                    AND msgid IS NOT NULL AND msgid <> ''
                )
                DELETE FROM isomessages WHERE id IN (SELECT id FROM duplicates_msgid);

                -- 2. Identify rows to delete due to TxId duplicates
                WITH duplicates_txid AS (
                    SELECT id 
                    FROM isomessages 
                    WHERE id NOT IN (
                        SELECT MAX(id)
                        FROM isomessages
                        WHERE txid IS NOT NULL AND txid <> ''
                        GROUP BY messagetype, txid
                    )
                    AND txid IS NOT NULL AND txid <> ''
                )
                DELETE FROM transactions WHERE isomessageid IN (SELECT id FROM duplicates_txid);

                WITH duplicates_txid AS (
                    SELECT id 
                    FROM isomessages 
                    WHERE id NOT IN (
                        SELECT MAX(id)
                        FROM isomessages
                        WHERE txid IS NOT NULL AND txid <> ''
                        GROUP BY messagetype, txid
                    )
                    AND txid IS NOT NULL AND txid <> ''
                )
                DELETE FROM isomessagestatuses WHERE isomessageid IN (SELECT id FROM duplicates_txid);

                WITH duplicates_txid AS (
                    SELECT id 
                    FROM isomessages 
                    WHERE id NOT IN (
                        SELECT MAX(id)
                        FROM isomessages
                        WHERE txid IS NOT NULL AND txid <> ''
                        GROUP BY messagetype, txid
                    )
                    AND txid IS NOT NULL AND txid <> ''
                )
                DELETE FROM isomessages WHERE id IN (SELECT id FROM duplicates_txid);
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
