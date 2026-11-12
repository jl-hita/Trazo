using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>
/// The model indexes the auth_challenges.UserId foreign key (EF convention); the original
/// identity migration did not create that index, so the snapshot and model had diverged.
/// </summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261113100000_AuthChallengeUserIdIndex")]
public sealed class AuthChallengeUserIdIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""CREATE INDEX IF NOT EXISTS "IX_auth_challenges_UserId" ON auth_challenges ("UserId");""");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""DROP INDEX IF EXISTS "IX_auth_challenges_UserId";""");
    }
}
