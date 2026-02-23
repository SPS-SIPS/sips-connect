using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddTransactionAddresses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "creditoraddress",
                table: "transactions",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "debtoraddress",
                table: "transactions",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "creditoraddress",
                table: "transactions");

            migrationBuilder.DropColumn(
                name: "debtoraddress",
                table: "transactions");
        }
    }
}
