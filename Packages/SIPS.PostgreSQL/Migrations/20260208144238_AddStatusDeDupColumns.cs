using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusDeDupColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_isomessagestatuses_isomessageid;");

            migrationBuilder.AddColumn<string>(
                name: "messagerole",
                table: "isomessagestatuses",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "msgid",
                table: "isomessagestatuses",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ux_iso_status_dedup",
                table: "isomessagestatuses",
                columns: new[] { "isomessageid", "messagerole", "status", "msgid" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_iso_status_dedup",
                table: "isomessagestatuses");

            migrationBuilder.DropColumn(
                name: "messagerole",
                table: "isomessagestatuses");

            migrationBuilder.DropColumn(
                name: "msgid",
                table: "isomessagestatuses");

            migrationBuilder.CreateIndex(
                name: "ix_isomessagestatuses_isomessageid",
                table: "isomessagestatuses",
                column: "isomessageid");
        }
    }
}
