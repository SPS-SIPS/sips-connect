using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddDomesticOutgoingLocalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "domesticrequestid",
                table: "isomessages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_domestic_local_id_lookup",
                table: "isomessages",
                columns: new[] { "frombic", "endtoendid" },
                filter: "\"messagetype\" = 'TransactionRequest' AND (\"businessservice\" IS NULL OR \"businessservice\" = '')");

            migrationBuilder.CreateIndex(
                name: "ux_domestic_outgoing_local_id",
                table: "isomessages",
                columns: new[] { "frombic", "domesticrequestid" },
                unique: true,
                filter: "\"domesticrequestid\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_domestic_local_id_lookup",
                table: "isomessages");

            migrationBuilder.DropIndex(
                name: "ux_domestic_outgoing_local_id",
                table: "isomessages");

            migrationBuilder.DropColumn(
                name: "domesticrequestid",
                table: "isomessages");
        }
    }
}
