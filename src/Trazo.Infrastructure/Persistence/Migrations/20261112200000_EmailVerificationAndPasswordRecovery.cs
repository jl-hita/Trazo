using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
[Migration("20261112200000_EmailVerificationAndPasswordRecovery")]
public sealed class EmailVerificationAndPasswordRecovery : Migration
{
 protected override void Up(MigrationBuilder migrationBuilder)
 {
  migrationBuilder.Sql("""
   ALTER TABLE users ADD COLUMN "EmailVerifiedAt" timestamp with time zone NULL;
   CREATE TABLE email_action_tokens (
    "Id" uuid PRIMARY KEY,
    "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
    "TokenHash" character varying(64) NOT NULL,
    "Purpose" character varying(30) NOT NULL,
    "CreatedAt" timestamp with time zone NOT NULL,
    "ExpiresAt" timestamp with time zone NOT NULL,
    "ConsumedAt" timestamp with time zone
   );
   CREATE UNIQUE INDEX "IX_email_action_tokens_TokenHash" ON email_action_tokens ("TokenHash");
   CREATE INDEX "IX_email_action_tokens_UserId_Purpose_ExpiresAt" ON email_action_tokens ("UserId", "Purpose", "ExpiresAt");
  """);
 }
 protected override void Down(MigrationBuilder migrationBuilder)
 {
  migrationBuilder.Sql("""
   DROP TABLE IF EXISTS email_action_tokens;
   ALTER TABLE users DROP COLUMN IF EXISTS "EmailVerifiedAt";
  """);
 }
}
