using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddUetrColumnToIsoMessages : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DO $$ 
                BEGIN 
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='isomessages' AND column_name='uetr') THEN 
                        ALTER TABLE isomessages ADD COLUMN uetr text; 
                    END IF; 
                END $$;");

            migrationBuilder.Sql(@"
                DO $$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_indexes WHERE indexname = 'ix_iso_msg_uetr') THEN
                        CREATE INDEX ix_iso_msg_uetr ON isomessages (uetr);
                    END IF;
                END $$;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_iso_msg_uetr",
                table: "isomessages");

            migrationBuilder.DropColumn(
                name: "uetr",
                table: "isomessages");
        }
    }
}
