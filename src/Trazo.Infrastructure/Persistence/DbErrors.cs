using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Trazo.Infrastructure.Persistence;

/// <summary>
/// Tells the database failures the code expects (two requests racing for the same unique key) from the ones it must not hide (the
/// database is down, a column overflowed). A <c>catch (DbUpdateException)</c> without this filter turns an outage into "someone else
/// already did it" and loses the exception.
/// </summary>
public static class DbErrors
{
    public static bool IsUniqueViolation(DbUpdateException exception)
    {
        for (Exception? e = exception; e is not null; e = e.InnerException)
        {
            if (e is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) return true;
            // SQLite, used by the test host, has no state code: its message is the only signal (the tests do not reference this assembly's provider).
            if (e.GetType().Name == "SqliteException" && e.Message.Contains("UNIQUE constraint failed", StringComparison.Ordinal)) return true;
        }
        return false;
    }
}
