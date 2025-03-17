using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class IntroducedReturnId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "returnid",
                table: "isomessages",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "returnid",
                table: "isomessages");
        }
    }
}
