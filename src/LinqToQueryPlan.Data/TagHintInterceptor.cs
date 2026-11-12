using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LinqToQueryPlan.Data;

// Section 7, rung 2. EF Core has no query hint API; this is the pattern Microsoft documents:
// tag the query, detect the tag in the first line of the command text, append the hint.
//
//   context.Hits.TagWith("hint:RECOMPILE").Where(...)                 -> OPTION (RECOMPILE)
//   context.Hits.TagWith("hint:OPTIMIZE FOR UNKNOWN").Where(...)      -> OPTION (OPTIMIZE FOR UNKNOWN)
//   context.Hits.TagWith("hint:USE HINT('DISABLE_PARAMETER_SNIFFING')") -> OPTION (USE HINT('DISABLE_PARAMETER_SNIFFING'))
//
// Cost worth saying on the slide: RECOMPILE and DISABLE_PARAMETER_SNIFFING both switch off PSP optimization for the statement.
public sealed class TagHintInterceptor : DbCommandInterceptor
{
    private const string Prefix = "-- hint:";

    public override InterceptionResult<DbDataReader> ReaderExecuting(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result)
    {
        Apply(command);
        return result;
    }

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        Apply(command);
        return ValueTask.FromResult(result);
    }

    private static void Apply(DbCommand command)
    {
        var text = command.CommandText;
        if (!text.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return;
        }

        var firstLineEnd = text.IndexOf('\n');
        var hint = (firstLineEnd < 0 ? text[Prefix.Length..] : text[Prefix.Length..firstLineEnd]).Trim();
        if (hint.Length == 0 || text.Contains("OPTION (", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        command.CommandText = text.TrimEnd().TrimEnd(';') + $" OPTION ({hint})";
    }
}
