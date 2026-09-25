using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <summary>
    /// Provenance of PAPSS status/return events: which values PAPSS reported (NETWORK_REPORTED), which the gateway reconstructed
    /// from stored state (LOCAL_RECONSTRUCTION), and the reference to the raw signed PAPSS message. Existing rows stay NULL
    /// (they predate provenance and are reported as such).
    /// </summary>
    public partial class AddPapssEventProvenance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "amountsource",
                table: "papss_operation_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "categorypurposesource",
                table: "papss_operation_events",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "fieldprovenance",
                table: "papss_operation_events",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rawevidencereference",
                table: "papss_operation_events",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "amountsource", table: "papss_operation_events");
            migrationBuilder.DropColumn(name: "categorypurposesource", table: "papss_operation_events");
            migrationBuilder.DropColumn(name: "fieldprovenance", table: "papss_operation_events");
            migrationBuilder.DropColumn(name: "rawevidencereference", table: "papss_operation_events");
        }
    }
}
