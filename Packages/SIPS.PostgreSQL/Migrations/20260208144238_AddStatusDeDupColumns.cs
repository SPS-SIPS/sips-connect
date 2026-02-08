using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SIPS.PostgreSQL.Migrations
{
    /// <inheritdoc />
    public partial class AddStatusDeDupColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_isomessagestatuses_isomessageid;");

            migrationBuilder.Sql(@"
                DO $$ 
                BEGIN 
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='isomessagestatuses' AND column_name='messagerole') THEN 
                        ALTER TABLE isomessagestatuses ADD COLUMN messagerole text NOT NULL DEFAULT ''; 
                    END IF; 
                END $$;");

            migrationBuilder.Sql(@"
                DO $$ 
                BEGIN 
                    IF NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='isomessagestatuses' AND column_name='msgid') THEN 
                        ALTER TABLE isomessagestatuses ADD COLUMN msgid text; 
                    END IF; 
                END $$;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_iso_status_dedup;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_iso_status_dedup ON isomessagestatuses (isomessageid, messagerole, status, msgid);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS ux_iso_status_dedup;");

            migrationBuilder.Sql(@"
                DO $$ 
                BEGIN 
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='isomessagestatuses' AND column_name='messagerole') THEN 
                        ALTER TABLE isomessagestatuses DROP COLUMN messagerole; 
                    END IF; 
                END $$;");

            migrationBuilder.Sql(@"
                DO $$ 
                BEGIN 
                    IF EXISTS (SELECT 1 FROM information_schema.columns WHERE table_name='isomessagestatuses' AND column_name='msgid') THEN 
                        ALTER TABLE isomessagestatuses DROP COLUMN msgid; 
                    END IF; 
                END $$;");

            migrationBuilder.Sql("DROP INDEX IF EXISTS ix_isomessagestatuses_isomessageid;");
            migrationBuilder.Sql("CREATE INDEX ix_isomessagestatuses_isomessageid ON isomessagestatuses (isomessageid);");
        }
    }
}
