using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Trazo.Infrastructure.Persistence.Migrations;

[DbContext(typeof(TrazoDbContext))]
[Migration("20261118000000_AddOriginalErrorTicketStackTrace")]
public partial class AddOriginalErrorTicketStackTrace : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OriginalStackTrace",
            table: "error_tickets",
            type: "character varying(16000)",
            maxLength: 16000,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "OriginalStackTrace",
            table: "error_tickets");
    }
}
