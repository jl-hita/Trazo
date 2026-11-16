using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Double opt-in for the waitlist and the invitation to the closed test: confirmation token (hash), confirmation and invitation dates.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261116100000_WaitlistTestInvites")]
public sealed class WaitlistTestInvites : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            ALTER TABLE waitlist_entries
                ADD COLUMN "ConfirmedAt" timestamp with time zone NULL,
                ADD COLUMN "ConfirmTokenHash" character varying(64) NULL,
                ADD COLUMN "ConfirmTokenExpiresAt" timestamp with time zone NULL,
                ADD COLUMN "InvitedAt" timestamp with time zone NULL;
            CREATE INDEX "IX_waitlist_entries_ConfirmTokenHash" ON waitlist_entries ("ConfirmTokenHash");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP INDEX IF EXISTS "IX_waitlist_entries_ConfirmTokenHash";
            ALTER TABLE waitlist_entries
                DROP COLUMN IF EXISTS "InvitedAt",
                DROP COLUMN IF EXISTS "ConfirmTokenExpiresAt",
                DROP COLUMN IF EXISTS "ConfirmTokenHash",
                DROP COLUMN IF EXISTS "ConfirmedAt";
        """);
    }
}
