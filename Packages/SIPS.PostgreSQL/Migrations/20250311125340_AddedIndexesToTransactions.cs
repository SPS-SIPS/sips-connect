using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddedIndexesToTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_transactions_endtoendid",
                table: "transactions",
                column: "endtoendid");

            migrationBuilder.CreateIndex(
                name: "ix_transactions_txid",
                table: "transactions",
                column: "txid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_transactions_endtoendid",
                table: "transactions");

            migrationBuilder.DropIndex(
                name: "ix_transactions_txid",
                table: "transactions");
        }
    }
}
