using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class ExtendedModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "endtoendid",
                table: "isomessages",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "round",
                table: "isomessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "txid",
                table: "isomessages",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "isomessagestatuses",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    status = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: true),
                    additionalinfo = table.Column<string>(type: "text", nullable: true),
                    date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    message = table.Column<byte[]>(type: "bytea", nullable: false),
                    response = table.Column<byte[]>(type: "bytea", nullable: true),
                    isomessageid = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_isomessagestatuses", x => x.id);
                    table.ForeignKey(
                        name: "fk_isomessagestatuses_isomessages_isomessageid",
                        column: x => x.isomessageid,
                        principalTable: "isomessages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_isomessagestatuses_isomessageid",
                table: "isomessagestatuses",
                column: "isomessageid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "isomessagestatuses");

            migrationBuilder.DropColumn(
                name: "endtoendid",
                table: "isomessages");

            migrationBuilder.DropColumn(
                name: "round",
                table: "isomessages");

            migrationBuilder.DropColumn(
                name: "txid",
                table: "isomessages");
        }
    }
}
