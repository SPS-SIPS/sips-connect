using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SIPS.PostgreSQL.Persistence;

#nullable disable

namespace SIPS.PostgreSQL.Migrations;

[DbContext(typeof(StorageBroker))]
[Migration("20260919090000_AddPapssDecisionOutbox")]
public partial class AddPapssDecisionOutbox : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "businessservice", table: "isomessages", type: "text", nullable: true);
        migrationBuilder.AddColumn<byte[]>(name: "papssdecision", table: "isomessages", type: "bytea", nullable: true);
        migrationBuilder.AddColumn<string>(name: "papssdecisionadmissioncode", table: "isomessages", type: "text", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "papssdecisionpublishedat", table: "isomessages", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<string>(name: "papssdecisionfailurecode", table: "isomessages", type: "text", nullable: true);
        migrationBuilder.AddColumn<DateTimeOffset>(name: "papssdecisionfailedat", table: "isomessages", type: "timestamp with time zone", nullable: true);
        migrationBuilder.CreateIndex(name: "ix_iso_msg_papss_decision_pending", table: "isomessages", columns: new[] { "businessservice", "papssdecisionpublishedat", "papssdecisionfailedat", "id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "ix_iso_msg_papss_decision_pending", table: "isomessages");
        migrationBuilder.DropColumn(name: "businessservice", table: "isomessages");
        migrationBuilder.DropColumn(name: "papssdecision", table: "isomessages");
        migrationBuilder.DropColumn(name: "papssdecisionadmissioncode", table: "isomessages");
        migrationBuilder.DropColumn(name: "papssdecisionpublishedat", table: "isomessages");
        migrationBuilder.DropColumn(name: "papssdecisionfailurecode", table: "isomessages");
        migrationBuilder.DropColumn(name: "papssdecisionfailedat", table: "isomessages");
    }
}
