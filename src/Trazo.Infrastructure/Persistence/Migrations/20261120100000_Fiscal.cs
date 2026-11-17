using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Trazo.Infrastructure.Persistence;

#nullable disable
namespace Trazo.Infrastructure.Persistence.Migrations;

/// <summary>Tax books of the owner: income and expense ledger, closed months and their audit trail.</summary>
[DbContext(typeof(TrazoDbContext))]
[Migration("20261120100000_Fiscal")]
public sealed class Fiscal : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            CREATE TABLE fiscal_entries (
                "Id" uuid PRIMARY KEY,
                "Kind" character varying(10) NOT NULL,
                "Date" date NOT NULL,
                "Period" character varying(7) NOT NULL,
                "Concept" character varying(200) NOT NULL,
                "Counterparty" character varying(150) NULL,
                "CounterpartyTaxId" character varying(30) NULL,
                "CounterpartyCountry" character varying(2) NULL,
                "InvoiceNumber" character varying(60) NULL,
                "Category" character varying(30) NOT NULL,
                "Base" numeric(14,2) NOT NULL,
                "VatTreatment" character varying(20) NOT NULL,
                "VatRatePct" numeric(5,2) NOT NULL,
                "VatAmount" numeric(14,2) NOT NULL,
                "WithholdingPct" numeric(5,2) NOT NULL,
                "WithholdingAmount" numeric(14,2) NOT NULL,
                "DeductiblePct" numeric(5,2) NOT NULL,
                "Total" numeric(14,2) NOT NULL,
                "Source" character varying(20) NOT NULL,
                "Notes" character varying(500) NULL,
                "CreatedBy" character varying(60) NULL,
                "CreatedAt" timestamp with time zone NOT NULL,
                "UpdatedAt" timestamp with time zone NOT NULL
            );
            CREATE INDEX "IX_fiscal_entries_Period_Kind_Date" ON fiscal_entries ("Period", "Kind", "Date");

            CREATE TABLE fiscal_closures (
                "Period" character varying(7) PRIMARY KEY,
                "ClosedAt" timestamp with time zone NOT NULL,
                "ClosedBy" character varying(60) NOT NULL,
                "EntryCount" integer NOT NULL,
                "SnapshotJson" character varying(4000) NOT NULL
            );

            CREATE TABLE fiscal_audit (
                "Id" uuid PRIMARY KEY,
                "Period" character varying(7) NOT NULL,
                "Action" character varying(12) NOT NULL,
                "Actor" character varying(60) NOT NULL,
                "Reason" character varying(300) NULL,
                "At" timestamp with time zone NOT NULL
            );
            CREATE INDEX "IX_fiscal_audit_At" ON fiscal_audit ("At");
        """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DROP TABLE IF EXISTS fiscal_audit;
            DROP TABLE IF EXISTS fiscal_closures;
            DROP TABLE IF EXISTS fiscal_entries;
        """);
    }
}
