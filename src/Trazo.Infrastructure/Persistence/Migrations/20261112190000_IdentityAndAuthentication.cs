using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable

namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
[Migration("20261112190000_IdentityAndAuthentication")]
public sealed class IdentityAndAuthentication : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE users (
                "Id" uuid PRIMARY KEY,
                "Email" character varying(320) NOT NULL,
                "NormalizedEmail" character varying(320) NOT NULL,
                "DisplayName" character varying(100) NOT NULL,
                "PasswordHash" character varying(512),
                "GoogleSubject" character varying(255),
                "Role" character varying(30) NOT NULL DEFAULT 'user',
                "TwoFactorEnabled" boolean NOT NULL DEFAULT false,
                "ProtectedAuthenticatorKey" character varying(1024),
                "FailedLoginAttempts" integer NOT NULL DEFAULT 0,
                "LockoutEnd" timestamp with time zone,
                "LastLoginAt" timestamp with time zone,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                CONSTRAINT "CK_users_role" CHECK ("Role" IN ('user', 'admin', 'superadmin'))
            );
            CREATE UNIQUE INDEX "IX_users_NormalizedEmail" ON users ("NormalizedEmail");
            CREATE UNIQUE INDEX "IX_users_GoogleSubject" ON users ("GoogleSubject") WHERE "GoogleSubject" IS NOT NULL;
            CREATE INDEX "IX_users_Role" ON users ("Role");

            CREATE TABLE refresh_tokens (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "TokenHash" character varying(64) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "RevokedAt" timestamp with time zone,
                "ReplacedByTokenHash" character varying(64),
                "DeviceName" character varying(120)
            );
            CREATE UNIQUE INDEX "IX_refresh_tokens_TokenHash" ON refresh_tokens ("TokenHash");
            CREATE INDEX "IX_refresh_tokens_UserId_ExpiresAt" ON refresh_tokens ("UserId", "ExpiresAt");

            CREATE TABLE auth_challenges (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "TokenHash" character varying(64) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "ExpiresAt" timestamp with time zone NOT NULL,
                "FailedAttempts" integer NOT NULL DEFAULT 0,
                "ConsumedAt" timestamp with time zone
            );
            CREATE UNIQUE INDEX "IX_auth_challenges_TokenHash" ON auth_challenges ("TokenHash");
            CREATE INDEX "IX_auth_challenges_ExpiresAt" ON auth_challenges ("ExpiresAt");

            CREATE TABLE auth_recovery_codes (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users("Id") ON DELETE CASCADE,
                "CodeHash" character varying(64) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UsedAt" timestamp with time zone
            );
            CREATE UNIQUE INDEX "IX_auth_recovery_codes_UserId_CodeHash" ON auth_recovery_codes ("UserId", "CodeHash");
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS auth_recovery_codes;
            DROP TABLE IF EXISTS auth_challenges;
            DROP TABLE IF EXISTS refresh_tokens;
            DROP TABLE IF EXISTS users;
            """);
    }
}
