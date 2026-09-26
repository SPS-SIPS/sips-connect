using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <summary>
    /// Supports the gateway's new RECALL_OUTCOME_UNRESOLVED signal (S3: reported on the existing recall-result callback/lookup
    /// path when the gateway could not read a definite ACCP/RJCT outcome from PAPSS for a camt.056 answer) and the operator
    /// recovery action (POST Recall/{recallId}/Close, final outcome RECALL_ABANDONED). No new column: both outcomes reuse
    /// papssoutcome. Rebuilds ux_papss_op_open_recall so a recall in RECALL_OUTCOME_UNRESOLVED also counts as OPEN (blocks a
    /// new recall on the same payment until it is resolved normally or closed manually).
    /// </summary>
    public partial class AddPapssRecallUnresolvedOutcome : Migration
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
                filter: "operation = 'RECALL' AND originaloperationid IS NOT NULL AND papssoutcome IN ('RECALL_PENDING', 'RECALL_ACCEPTED_BY_PAPSS', 'RECALL_OUTCOME_UNRESOLVED')");
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
                filter: "operation = 'RECALL' AND originaloperationid IS NOT NULL AND papssoutcome IN ('RECALL_PENDING', 'RECALL_ACCEPTED_BY_PAPSS')");
        }
    }
}
