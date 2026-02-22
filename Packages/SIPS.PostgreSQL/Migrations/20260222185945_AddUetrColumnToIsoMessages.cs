using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddUetrColumnToIsoMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "uetr",
                table: "isomessages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_iso_msg_uetr",
                table: "isomessages",
                column: "uetr");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_iso_msg_uetr",
                table: "isomessages");

            migrationBuilder.DropColumn(
                name: "uetr",
                table: "isomessages");
        }
    }
}
