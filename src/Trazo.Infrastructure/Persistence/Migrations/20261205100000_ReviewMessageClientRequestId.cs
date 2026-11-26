using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>The identity of a review message written by the app, unique inside its conversation. Purely additive.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261205100000_ReviewMessageClientRequestId")]
public sealed class ReviewMessageClientRequestId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        ALTER TABLE plan_review_messages ADD COLUMN "ClientRequestId" uuid NULL;
        CREATE UNIQUE INDEX "IX_plan_review_messages_RequestId_ClientRequestId" ON plan_review_messages ("RequestId", "ClientRequestId")
            WHERE "ClientRequestId" IS NOT NULL;
    """);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        DROP INDEX "IX_plan_review_messages_RequestId_ClientRequestId";
        ALTER TABLE plan_review_messages DROP COLUMN "ClientRequestId";
    """);
}
