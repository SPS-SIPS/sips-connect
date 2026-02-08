using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "isomessages",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    messagetype = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    frombic = table.Column<string>(type: "text", nullable: false),
                    tobic = table.Column<string>(type: "text", nullable: false),
                    message = table.Column<byte[]>(type: "bytea", nullable: false),
                    response = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_isomessages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transactions",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    type = table.Column<string>(type: "text", nullable: false),
                    isomessageid = table.Column<int>(type: "integer", nullable: false),
                    frombic = table.Column<string>(type: "text", nullable: false),
                    localinstrument = table.Column<string>(type: "text", nullable: false),
                    categorypurpose = table.Column<string>(type: "text", nullable: false),
                    endtoendid = table.Column<string>(type: "text", nullable: false),
                    txid = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "text", nullable: false),
                    debtorname = table.Column<string>(type: "text", nullable: false),
                    debtoraccount = table.Column<string>(type: "text", nullable: false),
                    debtoraccounttype = table.Column<string>(type: "text", nullable: false),
                    debtoragentbic = table.Column<string>(type: "text", nullable: false),
                    debtorissuer = table.Column<string>(type: "text", nullable: false),
                    creditorname = table.Column<string>(type: "text", nullable: false),
                    creditoraccount = table.Column<string>(type: "text", nullable: false),
                    creditoraccounttype = table.Column<string>(type: "text", nullable: false),
                    creditoragentbic = table.Column<string>(type: "text", nullable: false),
                    creditorissuer = table.Column<string>(type: "text", nullable: false),
                    remittanceinformation = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transactions", x => x.id);
                    table.ForeignKey(
                        name: "fk_transactions_isomessages_isomessageid",
                        column: x => x.isomessageid,
                        principalTable: "isomessages",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_transactions_isomessageid",
                table: "transactions",
                column: "isomessageid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "transactions");

            migrationBuilder.DropTable(
                name: "isomessages");
        }
    }
}
