using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;

namespace LinqToQueryPlan.Lab.Diagnostics;

// Runs the numbered scripts and the Query Store hint procedures. Guarded twice: a config switch and a local-server check.
// The scripts folder stays the single source of truth; the app splits each file on GO and runs the batches in order.
public sealed partial class LabAdmin(LabOptions options, string scriptsFolder)
{
    public string ScriptsFolder { get; } = scriptsFolder;

    public Task<ScriptResult> SetupAsync() => RunScriptsAsync("01-setup-database.sql", "03-xe-session.sql");
    public Task<ScriptResult> ResetAsync() => RunScriptsAsync("05-reset-between-demos.sql");
    public Task<ScriptResult> InflateAsync() => RunScriptsAsync("06-inflate-for-psp.sql");
    public Task<ScriptResult> CaseSetupAsync() => RunScriptsAsync("08-case-setup.sql");
    public Task<ScriptResult> CasePspOnAsync() => RunScriptsAsync("09-case-psp-on.sql");

    public async Task<ScriptResult> SetQueryStoreHintAsync(long queryId, string hint)
    {
        Guard();
        var allowed = new[] { "RECOMPILE", "OPTIMIZE FOR UNKNOWN", "USE HINT('DISABLE_PARAMETER_SNIFFING')", "USE HINT('DISABLE_PARAMETER_SENSITIVE_PLAN')" };
        if (!allowed.Contains(hint)) throw new ArgumentException($"Hint must be one of: {string.Join(", ", allowed)}");

        var sql = $"EXEC sys.sp_query_store_set_hints @query_id = {queryId}, @query_hints = N'OPTION ({hint.Replace("'", "''")})';";
        return await RunBatchesAsync("Query Store hint", [sql]);
    }

    public async Task<ScriptResult> ClearQueryStoreHintAsync(long queryId)
    {
        Guard();
        return await RunBatchesAsync("Clear Query Store hint", [$"EXEC sys.sp_query_store_clear_hints @query_id = {queryId};"]);
    }

    private async Task<ScriptResult> RunScriptsAsync(params string[] files)
    {
        Guard();
        var batches = new List<string>();
        foreach (var f in files)
        {
            var path = Path.Combine(ScriptsFolder, f);
            if (!File.Exists(path)) throw new FileNotFoundException($"Script not found: {path}. Check Lab:ScriptsPath.");
            var text = await File.ReadAllTextAsync(path);
            if (!options.DatabaseName.Equals("lahman2025", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Replace("lahman2025", options.DatabaseName);
            }
            batches.AddRange(GoSplitter().Split(text).Where(b => b.Trim().Length > 0 && !IsOnlyComments(b)));
        }
        return await RunBatchesAsync(string.Join(" + ", files), batches);
    }

    private async Task<ScriptResult> RunBatchesAsync(string name, IReadOnlyList<string> batches)
    {
        var result = new ScriptResult(name);
        var sw = Stopwatch.StartNew();
        await using var c = new SqlConnection(options.DiagnosticsConnectionString);
        c.InfoMessage += (_, e) => result.Messages.Add(e.Message);
        var open = c.OpenAsync();
        if (await Task.WhenAny(open, Task.Delay(TimeSpan.FromSeconds(20))) != open)
        {
            _ = open.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            throw new TimeoutException("Couldn't open a connection to SQL Server in 20 seconds. Run the connection check first.");
        }
        await open;

        // ALTER DATABASE waits for every other session in the database to let go. Without a limit, one idle
        // transaction in another tool makes Setup wait forever, and every new connection then queues behind it.
        await using (var lockCmd = new SqlCommand("SET LOCK_TIMEOUT 15000;", c))
        {
            await lockCmd.ExecuteNonQueryAsync();
        }

        foreach (var batch in batches)
        {
            await using var cmd = new SqlCommand(batch, c) { CommandTimeout = options.AdminTimeoutSeconds };
            await using var r = await cmd.ExecuteReaderAsync();
            do
            {
                if (r.FieldCount == 0) continue;
                var table = new List<string[]> { Enumerable.Range(0, r.FieldCount).Select(r.GetName).ToArray() };
                while (await r.ReadAsync())
                {
                    table.Add(Enumerable.Range(0, r.FieldCount).Select(i => r.IsDBNull(i) ? "NULL" : Convert.ToString(r.GetValue(i)) ?? "").ToArray());
                }
                result.ResultSets.Add(table);
            } while (await r.NextResultAsync());
            result.Batches++;
        }

        result.ElapsedMs = sw.ElapsedMilliseconds;
        return result;
    }

    private void Guard()
    {
        if (!options.CanRunAdminActions(out var reason)) throw new InvalidOperationException(reason);
    }

    private static bool IsOnlyComments(string batch) =>
        batch.Split('\n').All(l => l.Trim().Length == 0 || l.TrimStart().StartsWith("--", StringComparison.Ordinal));

    [GeneratedRegex(@"^\s*GO\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase)]
    private static partial Regex GoSplitter();
}

public sealed class ScriptResult(string name)
{
    public string Name { get; } = name;
    public int Batches { get; set; }
    public long ElapsedMs { get; set; }
    public List<string> Messages { get; } = [];
    public List<List<string[]>> ResultSets { get; } = [];
}
