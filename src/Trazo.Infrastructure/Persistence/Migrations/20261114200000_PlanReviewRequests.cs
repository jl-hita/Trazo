using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Manual review of a plan by an administrator: the request, who has it, and the conversation with the user.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261114200000_PlanReviewRequests")]
public sealed class PlanReviewRequests : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE plan_review_requests (
                "Id" uuid PRIMARY KEY,
                "UserId" uuid NOT NULL REFERENCES users ("Id") ON DELETE CASCADE,
                "PlanId" uuid NOT NULL REFERENCES training_plans ("Id") ON DELETE CASCADE,
                "Status" character varying(20) NOT NULL,
                "AssignedToUserId" uuid NULL REFERENCES users ("Id") ON DELETE SET NULL,
                "AssignedAt" timestamp with time zone NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL,
                "ReviewedAt" timestamp with time zone NULL,
                "UserLastReadAt" timestamp with time zone NULL
            );
            CREATE INDEX "IX_plan_review_requests_Status_UpdatedAt" ON plan_review_requests ("Status", "UpdatedAt");
            CREATE INDEX "IX_plan_review_requests_UserId_CreatedAt" ON plan_review_requests ("UserId", "CreatedAt");
            CREATE INDEX "IX_plan_review_requests_AssignedToUserId" ON plan_review_requests ("AssignedToUserId");
            CREATE UNIQUE INDEX "IX_plan_review_requests_PlanId" ON plan_review_requests ("PlanId") WHERE "Status" <> 'Reviewed';

            CREATE TABLE plan_review_messages (
                "Id" uuid PRIMARY KEY,
                "RequestId" uuid NOT NULL REFERENCES plan_review_requests ("Id") ON DELETE CASCADE,
                "Author" character varying(10) NOT NULL,
                "AuthorUserId" uuid NULL REFERENCES users ("Id") ON DELETE SET NULL,
                "Body" character varying(2000) NOT NULL,
                "CreatedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX "IX_plan_review_messages_RequestId_CreatedAt" ON plan_review_messages ("RequestId", "CreatedAt");
            CREATE INDEX "IX_plan_review_messages_AuthorUserId" ON plan_review_messages ("AuthorUserId");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE IF EXISTS plan_review_messages; DROP TABLE IF EXISTS plan_review_requests;");
}
