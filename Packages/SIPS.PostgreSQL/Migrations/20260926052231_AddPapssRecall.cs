using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <summary>
    /// PAPSS recall (camt.056): RECALL operations reuse papss_operations (operation = 'RECALL', linked to the recalled payment by
    /// originaloperationid) and the new papssoutcome values RECALL_*; the only schema change is the partial unique index that
    /// allows at most one OPEN recall (RECALL_PENDING / RECALL_ACCEPTED_BY_PAPSS) per payment.
    /// </summary>
    public partial class AddPapssRecall : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ux_papss_op_open_recall",
                table: "papss_operations",
                column: "originaloperationid",
                unique: true,
                filter: "operation = 'RECALL' AND originaloperationid IS NOT NULL AND papssoutcome IN ('RECALL_PENDING', 'RECALL_ACCEPTED_BY_PAPSS')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_papss_op_open_recall",
                table: "papss_operations");
        }
    }
}
