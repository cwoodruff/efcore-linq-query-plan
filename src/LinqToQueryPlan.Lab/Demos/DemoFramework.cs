using System.Diagnostics;
using LinqToQueryPlan.Data;
using LinqToQueryPlan.Lab.Diagnostics;
using LinqToQueryPlan.Lab.Model;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

[Flags]
public enum ServerPanels
{
    None = 0,
    PlanCache = 1,
    QueryStore = 2,
    ExtendedEvents = 4,
}

public sealed class DemoInfo
{
    public required int Number { get; init; }
    public required string Slug { get; init; }
    public required string Title { get; init; }
    public required string TalkSection { get; init; }
    public required string Proves { get; init; }
    public required string NotesFile { get; init; }
    public required string TagPrefix { get; init; }
    public ServerPanels Panels { get; init; }
    public required IReadOnlyList<DemoAction> Actions { get; init; }
}

public sealed class DemoAction
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public required string Description { get; init; }
    public IReadOnlyList<DemoInput> Inputs { get; init; } = [];
    public bool RequiresAdmin { get; init; }
    public required Func<LabSession, ActionArgs, Task> Run { get; init; }
}

public sealed record DemoInput(string Name, string Label, string Default, IReadOnlyList<string>? Options = null, string? Help = null);

public sealed class ActionArgs(IReadOnlyDictionary<string, string?> values)
{
    public string Get(string name, string fallback) =>
        values.TryGetValue(name, out var v) && !string.IsNullOrWhiteSpace(v) ? v.Trim() : fallback;

    public short GetShort(string name, short fallback) => short.TryParse(Get(name, ""), out var v) ? v : fallback;
    public long GetLong(string name, long fallback) => long.TryParse(Get(name, ""), out var v) ? v : fallback;
}

// One action's worth of work: creates contexts that log into the result, runs queries, and pulls back their plans.
public sealed class LabSession(LabOptions options, ServerDiagnostics diagnostics, LabAdmin admin, RunResult result)
{
    public LabOptions Options { get; } = options;
    public ServerDiagnostics Diagnostics { get; } = diagnostics;
    public LabAdmin Admin { get; } = admin;
    public RunResult Result { get; } = result;

    // What the action is waiting on right now. If the action times out, this is what the error card reports.
    public string Step { get; set; } = "starting";

    public LahmanContext CreateContext(Func<ContextSettings, ContextSettings>? configure = null, List<string>? logSink = null)
    {
        var sink = logSink ?? Result.EfLog;
        var settings = new ContextSettings
        {
            ConnectionString = Options.DemoConnectionString,
            Log = line => { lock (sink) sink.Add(line.Trim()); },
        };
        return LahmanContextFactory.Create(configure is null ? settings : configure(settings));
    }

    // Runs a LINQ query exactly the way an app would (ToListAsync), times it, and captures the plan it ran with.
    public async Task<(QueryRun Run, List<T> Rows)> RunAsync<T>(string label, IQueryable<T> query, string tag, bool capturePlan = true)
    {
        var (sql, sqlError) = TryToQueryString(query);
        Step = $"reading the plan cache before '{label}' (diagnostics connection)";
        var before = capturePlan ? await Diagnostics.SnapshotAsync(tag) : null;
        Step = $"running '{label}' through EF Core";
        var sw = Stopwatch.StartNew();
        var rows = await query.ToListAsync();
        sw.Stop();

        var run = new QueryRun { Label = label, Tag = tag, Sql = sql, ElapsedMs = sw.ElapsedMilliseconds, Rows = rows.Count };
        await Finish(run, before, sqlError);
        return (run, rows);
    }

    public async Task<QueryRun> RunCountAsync<T>(string label, IQueryable<T> query, string tag, bool capturePlan = true)
    {
        var (sql, sqlError) = TryToQueryString(query);
        Step = $"reading the plan cache before '{label}' (diagnostics connection)";
        var before = capturePlan ? await Diagnostics.SnapshotAsync(tag) : null;
        Step = $"running '{label}' through EF Core";
        var sw = Stopwatch.StartNew();
        var count = await query.CountAsync();
        sw.Stop();

        var run = new QueryRun { Label = label, Tag = tag, Sql = sql, ElapsedMs = sw.ElapsedMilliseconds, Rows = count };
        await Finish(run, before, sqlError);
        return run;
    }

    // Runs SQL text the way a query window would: one ad hoc batch, no sp_executesql, local variables instead of parameters.
    // Used for the false clue: what you get when you paste ToQueryString() output into VS Code and run it.
    public async Task<QueryRun> RunSqlTextAsync(string label, string sql, string tag)
    {
        Step = $"reading the plan cache before '{label}' (diagnostics connection)";
        var before = await Diagnostics.SnapshotAsync(tag);
        Step = $"running '{label}' as an ad hoc batch";
        var sw = Stopwatch.StartNew();
        var rows = 0;
        await using (var c = new Microsoft.Data.SqlClient.SqlConnection(Options.DemoConnectionString))
        {
            await c.OpenAsync();
            await using var cmd = new Microsoft.Data.SqlClient.SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            do { while (await r.ReadAsync()) rows++; } while (await r.NextResultAsync());
        }
        sw.Stop();

        var run = new QueryRun { Label = label, Tag = tag, Sql = sql, ElapsedMs = sw.ElapsedMilliseconds, Rows = rows };
        await Finish(run, before, null);
        return run;
    }

    private async Task Finish(QueryRun run, PlanSnapshot? before, string? sqlError)
    {
        if (run.Sql is not null)
        {
            foreach (System.Text.RegularExpressions.Match m in SqlText.Declare().Matches(run.Sql))
                run.SentValues[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }

        Step = $"reading the plan for '{run.Label}' (diagnostics connection)";
        if (before is not null) run.Plans.AddRange(await Diagnostics.CapturePlansAsync(run.Tag, before));

        if (run.Sql is null && run.Plans.LastOrDefault() is { } p)
        {
            run.Sql = "-- ToQueryString() failed here, so this is the statement text from the plan cache instead.\n" + p.Parsed.StatementText;
            Result.Warn($"{run.Label}: ToQueryString() threw ({sqlError}). The SQL shown is what the server cached.");
        }
        Result.Queries.Add(run);
    }

    private static (string? Sql, string? Error) TryToQueryString(IQueryable query)
    {
        try { return (query.ToQueryString(), null); }
        catch (Exception ex) { return (null, $"{ex.GetType().Name}: {ex.Message}"); }
    }
}

public static partial class SqlText
{
    // DECLARE @key nvarchar(20) = N'rosepe01';   ->   @key = N'rosepe01'
    [System.Text.RegularExpressions.GeneratedRegex(@"^DECLARE\s+(@\w+)\s+[^=]+=\s*(.+?);\s*$", System.Text.RegularExpressions.RegexOptions.Multiline)]
    public static partial System.Text.RegularExpressions.Regex Declare();

    [System.Text.RegularExpressions.GeneratedRegex(@"^CAST\((.*) AS [^)]+\)$")]
    private static partial System.Text.RegularExpressions.Regex Cast();

    // Makes "(2020)", "CAST(2020 AS smallint)", and "2020" compare equal.
    public static string Normalize(string v)
    {
        v = v.Trim();
        var m = Cast().Match(v);
        if (m.Success) v = m.Groups[1].Value;
        return v.Trim('(', ')', ' ');
    }
}

public static class DemoRunner
{
    public static async Task<RunResult> RunAsync(DemoInfo demo, DemoAction action, IReadOnlyDictionary<string, string?> values,
        LabOptions options, ServerDiagnostics diagnostics, LabAdmin admin)
    {
        var result = new RunResult { DemoSlug = demo.Slug, ActionLabel = action.Label };
        var sw = Stopwatch.StartNew();
        try
        {
            if (action.RequiresAdmin && !options.CanRunAdminActions(out var reason))
            {
                result.Error = "This action changes server state and is turned off here.";
                result.ErrorHint = reason;
                return result;
            }

            var session = new LabSession(options, diagnostics, admin, result);
            var limit = TimeSpan.FromSeconds(action.RequiresAdmin ? options.AdminTimeoutSeconds : options.ActionTimeoutSeconds);
            var work = action.Run(session, new ActionArgs(values));
            if (await Task.WhenAny(work, Task.Delay(limit)) != work)
            {
                // The query can't be cancelled from here, but the browser gets an answer.
                _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
                result.Error = $"No answer from SQL Server after {limit.TotalSeconds:N0} seconds while {session.Step}.";
                result.ErrorHint = "Run the connection check on the Lab page. If the connection is fine, something on the server is blocking: " +
                                   "an open transaction in another tool (VS Code, Rider) or an earlier request that is still waiting.";
                result.TotalMs = sw.ElapsedMilliseconds;
                return result;
            }
            await work;
        }
        catch (Exception ex)
        {
            result.Error = ex.GetBaseException().Message;
            result.ErrorHint = Hint(ex);
        }
        result.TotalMs = sw.ElapsedMilliseconds;
        return result;
    }

    private static string? Hint(Exception ex)
    {
        var msg = ex.GetBaseException().Message;
        if (msg.Contains("Invalid object name", StringComparison.OrdinalIgnoreCase))
            return "A table the demo expects is missing. Check the Lab page; the Hits table only exists after script 06.";
        if (msg.Contains("Login failed", StringComparison.OrdinalIgnoreCase) || msg.Contains("network-related", StringComparison.OrdinalIgnoreCase))
            return "Can't reach SQL Server. Check the connection string (Lab:ConnectionString or LAHMAN_CONNECTION) and that the container is running.";
        if (msg.Contains("Lock request time out", StringComparison.OrdinalIgnoreCase))
            return "Another session is holding the database (an open transaction in VS Code or Rider, or an earlier request). Close it, then try again. On a laptop, restarting the container clears it.";
        if (msg.Contains("Connection Timeout Expired", StringComparison.OrdinalIgnoreCase) || msg.Contains("timeout period elapsed", StringComparison.OrdinalIgnoreCase) || msg.Contains("error 258", StringComparison.OrdinalIgnoreCase))
            return "SQL Server didn't answer in time. Run the connection check on the Lab page; it tries each step separately and tells you which one stalls.";
        if (msg.Contains("event session", StringComparison.OrdinalIgnoreCase))
            return "The Extended Events session doesn't exist yet. Run Setup on the Lab page (scripts 01 and 03).";
        if (msg.Contains("VIEW SERVER", StringComparison.OrdinalIgnoreCase) || msg.Contains("permission was denied", StringComparison.OrdinalIgnoreCase))
            return "The plan and Query Store views need VIEW SERVER PERFORMANCE STATE. In a local container, connect as sa.";
        return null;
    }
}
