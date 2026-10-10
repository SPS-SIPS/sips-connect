using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <summary>
    /// R2: a counterparty recalling a payment this institution RECEIVED. Reuses papss_operations (operation = 'RECALL',
    /// direction = 'INBOUND', linked to the received payment by originaloperationid) with its own, separate PapssOutcome
    /// state machine (INBOUND_RECALL_*) -- no new column, same pattern as AddPapssRecallUnresolvedOutcome. The only schema
    /// change is widening ux_papss_op_open_recall so an inbound recall also counts as OPEN (at most one open recall, in
    /// either direction, per specific stored payment row).
    /// </summary>
    public partial class AddPapssInboundRecall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_papss_op_open_recall",
                table: "papss_operations");

            migrationBuilder.CreateIndex(
                name: "ux_papss_op_open_recall",
                table: "papss_operations",
                column: "originaloperationid",
                unique: true,
                filter: "operation = 'RECALL' AND originaloperationid IS NOT NULL AND papssoutcome IN ('RECALL_PENDING', 'RECALL_ACCEPTED_BY_PAPSS', 'RECALL_OUTCOME_UNRESOLVED', 'INBOUND_RECALL_AWAITING_DECISION', 'INBOUND_RECALL_ACCEPTED_BY_BANK', 'INBOUND_RECALL_REJECTED_BY_BANK', 'INBOUND_RECALL_UNRESOLVED')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_papss_op_open_recall",
                table: "papss_operations");

            migrationBuilder.CreateIndex(
                name: "ux_papss_op_open_recall",
                table: "papss_operations",
                column: "originaloperationid",
                unique: true,
                filter: "operation = 'RECALL' AND originaloperationid IS NOT NULL AND papssoutcome IN ('RECALL_PENDING', 'RECALL_ACCEPTED_BY_PAPSS', 'RECALL_OUTCOME_UNRESOLVED')");
        }
    }
}
