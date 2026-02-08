using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class IntroducedExternalIdsToMessage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "bizmsgidr",
                table: "isomessages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "msgdefidr",
                table: "isomessages",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "msgid",
                table: "isomessages",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bizmsgidr",
                table: "isomessages");

            migrationBuilder.DropColumn(
                name: "msgdefidr",
                table: "isomessages");

            migrationBuilder.DropColumn(
                name: "msgid",
                table: "isomessages");
        }
    }
}
