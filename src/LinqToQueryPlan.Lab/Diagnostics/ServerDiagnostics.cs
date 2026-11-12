using System.Data;
using System.Xml.Linq;
using LinqToQueryPlan.Lab.Model;
using Microsoft.Data.SqlClient;

namespace LinqToQueryPlan.Lab.Diagnostics;

// Read-only views of what the server did: plan cache, Query Store, Extended Events, configuration.
// Every query here runs under the Diagnostics application name so it never appears in the demo XE trace.
public sealed class ServerDiagnostics(LabOptions options)
{
    private string Cs => options.DiagnosticsConnectionString;

    public async Task<DateTime> ServerNowAsync()
    {
        await using var c = await OpenAsync();
        await using var cmd = new SqlCommand("SELECT SYSDATETIME();", c);
        return (DateTime)(await cmd.ExecuteScalarAsync())!;
    }

    // ---------------------------------------------------------------------------------------------
    // The plan EF actually got.
    //
    // A statement belongs to a tag when its batch text contains the tag comment, or when it is a
    // Parameter Sensitive Plan query variant of such a statement. Variants are cached as their own prepared
    // statements whose text starts at SELECT: the TagWith comment does not follow them. They share the
    // dispatcher's query_hash, so they are matched on that.
    //
    // Which statements did *this* run execute? Take a snapshot of (plan_handle, statement offset, execution_count)
    // before the query, compare after. A time window is not good enough: the previous query in a loop can start
    // within the same few milliseconds.
    // ---------------------------------------------------------------------------------------------
    private const string TaggedStatements = """
        WITH tagged AS (
            SELECT cp.plan_handle
            FROM sys.dm_exec_cached_plans AS cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) AS st
            WHERE st.text LIKE N'%-- ' + @tag + N'%'
              AND st.text NOT LIKE N'%dm_exec_cached_plans%'
        ),
        hashes AS (
            SELECT DISTINCT CONVERT(binary(8), s.n.value('@QueryHash', 'varchar(20)'), 1) AS query_hash
            FROM tagged AS t
            CROSS APPLY sys.dm_exec_query_plan(t.plan_handle) AS qp
            CROSS APPLY qp.query_plan.nodes('declare default element namespace "http://schemas.microsoft.com/sqlserver/2004/07/showplan"; //StmtSimple[@QueryHash]') AS s(n)
        ),
        stmts AS (
            SELECT qs.*
            FROM sys.dm_exec_query_stats AS qs
            CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
            WHERE (st.text LIKE N'%-- ' + @tag + N'%' OR qs.query_hash IN (SELECT query_hash FROM hashes WHERE query_hash IS NOT NULL))
              AND st.text NOT LIKE N'%dm_exec_query_stats%'
        )
        """;

    public async Task<PlanSnapshot> SnapshotAsync(string tag)
    {
        var sql = TaggedStatements + """
            SELECT CONVERT(varchar(140), plan_handle, 1) + ':' + CAST(statement_start_offset AS varchar(12)), execution_count FROM stmts;
            SELECT SYSDATETIME();
            """;
        await using var c = await OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.Add(new SqlParameter("@tag", SqlDbType.NVarChar, 200) { Value = tag });
        await using var r = await cmd.ExecuteReaderAsync();
        var counts = new Dictionary<string, long>();
        while (await r.ReadAsync()) counts[r.GetString(0)] = r.GetInt64(1);
        await r.NextResultAsync();
        await r.ReadAsync();
        return new PlanSnapshot(r.GetDateTime(0), counts);
    }

    public async Task<List<CapturedPlan>> CapturePlansAsync(string tag, PlanSnapshot before)
    {
        var sql = TaggedStatements + """
            SELECT
                CONVERT(varchar(140), qs.plan_handle, 1) + ':' + CAST(qs.statement_start_offset AS varchar(12)) AS stmt_key,
                qs.creation_time, qs.last_execution_time, qs.execution_count, qs.last_rows, qs.last_logical_reads,
                qs.last_elapsed_time, CONVERT(varchar(20), qs.query_hash, 1) AS query_hash,
                CONVERT(varchar(20), qs.query_plan_hash, 1) AS plan_hash,
                CAST(qps.query_plan AS nvarchar(max)) AS actual_plan,
                CAST(qp.query_plan AS nvarchar(max)) AS cached_plan
            FROM stmts AS qs
            OUTER APPLY sys.dm_exec_query_plan_stats(qs.plan_handle) AS qps
            OUTER APPLY sys.dm_exec_query_plan(qs.plan_handle) AS qp
            WHERE qs.last_execution_time >= DATEADD(second, -1, @since)
            ORDER BY qs.last_execution_time;
            """;

        await using var c = await OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.Add(new SqlParameter("@tag", SqlDbType.NVarChar, 200) { Value = tag });
        cmd.Parameters.Add(new SqlParameter("@since", SqlDbType.DateTime2) { Value = before.ServerTime });

        var plans = new List<CapturedPlan>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync())
        {
            var key = r.GetString(0);
            var execs = r.GetInt64(3);
            var existed = before.ExecutionCounts.TryGetValue(key, out var previous);
            if (existed && previous >= execs) continue;   // didn't run during this query

            var actualXml = r.IsDBNull(9) ? null : r.GetString(9);
            var cachedXml = r.IsDBNull(10) ? null : r.GetString(10);

            ParsedPlan? parsed = null;
            string? xml = null;
            var isActual = false;

            if (actualXml is not null)
            {
                var p = ShowplanParser.Parse(actualXml);
                if (p.NodeCount > 1 || (p.Root is not null && cachedXml is null))
                {
                    parsed = p; xml = actualXml; isActual = true;
                }
            }
            if (parsed is null && cachedXml is not null)
            {
                parsed = ShowplanParser.Parse(cachedXml); xml = cachedXml;
            }
            if (parsed is null || xml is null) continue;

            plans.Add(new CapturedPlan
            {
                PlanXml = xml,
                Parsed = parsed,
                IsActual = isActual,
                CreationTime = r.GetDateTime(1),
                LastExecutionTime = r.GetDateTime(2),
                ExecutionCount = execs,
                LastRows = r.GetInt64(4),
                LastLogicalReads = r.GetInt64(5),
                LastElapsedMs = r.GetInt64(6) / 1000.0,
                QueryHash = r.GetString(7),
                PlanHash = r.GetString(8),
                CompiledForThisRun = !existed,
            });
        }
        return plans;
    }

    // ---------------------------------------------------------------------------------------------
    // Plan cache grouped by tag line (scripts/02-plan-cache-by-tag.sql, first query).
    // ---------------------------------------------------------------------------------------------
    public async Task<List<PlanCacheGroup>> PlanCacheByTagAsync(string tagPrefix)
    {
        const string sql = """
            SELECT cp.usecounts, cp.size_in_bytes, cp.objtype, st.text
            FROM sys.dm_exec_cached_plans AS cp
            CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) AS st
            WHERE st.text LIKE N'%-- ' + @tag + N'%'
              AND st.text NOT LIKE N'%dm_exec_cached_plans%';
            """;

        await using var c = await OpenAsync();
        await using var cmd = new SqlCommand(sql, c);
        cmd.Parameters.Add(new SqlParameter("@tag", SqlDbType.NVarChar, 200) { Value = tagPrefix });

        var rows = new List<(int uses, int bytes, string objtype, string text)>();
        await using var r = await cmd.ExecuteReaderAsync();
        while (await r.ReadAsync()) rows.Add((r.GetInt32(0), r.GetInt32(1), r.GetString(2), r.GetString(3)));

        return rows
            .GroupBy(x => TagLine(x.text, tagPrefix))
            .Select(g => new PlanCacheGroup(g.Key, g.Count(), g.Sum(x => x.uses), g.Sum(x => x.bytes) / 1024.0,
                string.Join(", ", g.Select(x => x.objtype).Distinct())))
            .OrderBy(g => g.TagLine)
            .ToList();
    }

    private static string TagLine(string text, string prefix)
    {
        var i = text.IndexOf("-- " + prefix, StringComparison.Ordinal);
        if (i < 0) return prefix;
        var end = text.IndexOfAny(['\r', '\n'], i);
        return (end < 0 ? text[(i + 3)..] : text[(i + 3)..end]).Trim();
    }

    // ---------------------------------------------------------------------------------------------
    // Query Store (scripts/04-query-store.sql sections A, B, C).
    // sp_query_store_flush_db pushes in-memory runtime stats to the catalog views so a just-run query shows up.
    // ---------------------------------------------------------------------------------------------
    public async Task<QueryStoreView> QueryStoreAsync(string tagPrefix)
    {
        var view = new QueryStoreView();
        await using var c = await OpenAsync();

        try
        {
            await using var flush = new SqlCommand("EXEC sys.sp_query_store_flush_db;", c);
            await flush.ExecuteNonQueryAsync();
        }
        catch (SqlException ex)
        {
            view.Notes.Add($"Could not flush Query Store ({ex.Message}). Results may lag by a minute.");
        }

        // Query Store keeps statement text, and the statement starts at SELECT: your TagWith comment is not in
        // query_sql_text. The batch it was compiled from still has it, via last_compile_batch_sql_handle.
        // PSP variants have no tag at all; they are found through sys.query_store_query_variant.
        const string sql = """
            WITH tagged AS (
                SELECT q.query_id
                FROM sys.query_store_query AS q
                CROSS APPLY sys.dm_exec_sql_text(q.last_compile_batch_sql_handle) AS bt
                WHERE bt.text LIKE N'%-- ' + @tag + N'%'
                  AND bt.text NOT LIKE N'%query_store%'
            ),
            ids AS (
                SELECT query_id, CAST(NULL AS bigint) AS parent_query_id FROM tagged
                UNION
                SELECT v.query_variant_query_id, v.parent_query_id
                FROM sys.query_store_query_variant AS v
                WHERE v.parent_query_id IN (SELECT query_id FROM tagged)
            )
            SELECT q.query_id,
                   MAX(ids.parent_query_id) AS parent_query_id,
                   COUNT(DISTINCT p.plan_id) AS plans,
                   ISNULL(SUM(rs.count_executions), 0) AS executions,
                   MAX(rs.last_execution_time) AS last_exec,
                   MAX(h.query_hint_text) AS hint,
                   LEFT(MAX(qt.query_sql_text), 400) AS sql_preview,
                   MAX(q.query_parameterization_type_desc) AS parameterization
            FROM ids
            JOIN sys.query_store_query AS q ON q.query_id = ids.query_id
            JOIN sys.query_store_query_text AS qt ON qt.query_text_id = q.query_text_id
            LEFT JOIN sys.query_store_plan AS p ON p.query_id = q.query_id
            LEFT JOIN sys.query_store_runtime_stats AS rs ON rs.plan_id = p.plan_id
            LEFT JOIN sys.query_store_query_hints AS h ON h.query_id = q.query_id
            GROUP BY q.query_id
            ORDER BY CASE WHEN MAX(ids.parent_query_id) IS NOT NULL THEN 2
                          WHEN MAX(q.query_parameterization_type_desc) = N'User' THEN 0 ELSE 1 END,
                     MAX(rs.last_execution_time) DESC;

            WITH tagged AS (
                SELECT q.query_id
                FROM sys.query_store_query AS q
                CROSS APPLY sys.dm_exec_sql_text(q.last_compile_batch_sql_handle) AS bt
                WHERE bt.text LIKE N'%-- ' + @tag + N'%'
                  AND bt.text NOT LIKE N'%query_store%'
            )
            SELECT v.parent_query_id, v.dispatcher_plan_id, v.query_variant_query_id, p.plan_id, p.plan_type_desc,
                   ISNULL(SUM(rs.count_executions), 0) AS executions,
                   AVG(rs.avg_logical_io_reads) AS avg_reads,
                   AVG(rs.avg_duration) / 1000.0 AS avg_ms
            FROM sys.query_store_query_variant AS v
            JOIN sys.query_store_plan AS p ON p.query_id = v.query_variant_query_id
            LEFT JOIN sys.query_store_runtime_stats AS rs ON rs.plan_id = p.plan_id
            WHERE v.parent_query_id IN (SELECT query_id FROM tagged)
            GROUP BY v.parent_query_id, v.dispatcher_plan_id, v.query_variant_query_id, p.plan_id, p.plan_type_desc
            ORDER BY v.parent_query_id, v.query_variant_query_id;
            """;

        await using (var cmd = new SqlCommand(sql, c))
        {
            cmd.Parameters.Add(new SqlParameter("@tag", SqlDbType.NVarChar, 200) { Value = tagPrefix });
            await using var r = await cmd.ExecuteReaderAsync();
            while (await r.ReadAsync())
            {
                view.Queries.Add(new QueryStoreQuery(
                    r.GetInt64(0), r.IsDBNull(1) ? null : r.GetInt64(1), r.GetInt32(2), r.GetInt64(3),
                    r.IsDBNull(4) ? null : r.GetDateTimeOffset(4),
                    r.IsDBNull(5) ? null : r.GetString(5),
                    r.GetString(6),
                    r.IsDBNull(7) ? "" : r.GetString(7)));
            }
            await r.NextResultAsync();
            while (await r.ReadAsync())
            {
                view.Variants.Add(new QueryStoreVariant(
                    r.GetInt64(0), r.GetInt64(1), r.GetInt64(2), r.GetInt64(3), r.GetString(4),
                    r.GetInt64(5), r.IsDBNull(6) ? 0 : r.GetDouble(6), r.IsDBNull(7) ? 0 : r.GetDouble(7)));
            }
        }

        return view;
    }

    // ---------------------------------------------------------------------------------------------
    // Extended Events ring buffer (scripts/07-read-xe-ring-buffer.sql).
    // ---------------------------------------------------------------------------------------------
    public async Task<XeView> ReadXeAsync(string? tagFilter)
    {
        var view = new XeView { Tag = tagFilter ?? "" };
        await using var c = await OpenAsync();

        const string stateSql = """
            SELECT CASE WHEN EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'LinqToQueryPlan') THEN 1 ELSE 0 END,
                   CASE WHEN EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE name = N'LinqToQueryPlan') THEN 1 ELSE 0 END;
            """;
        await using (var cmd = new SqlCommand(stateSql, c))
        await using (var r = await cmd.ExecuteReaderAsync())
        {
            await r.ReadAsync();
            view.SessionExists = r.GetInt32(0) == 1;
            view.SessionRunning = r.GetInt32(1) == 1;
        }
        if (!view.SessionRunning) return view;

        const string bufferSql = """
            SELECT CAST(t.target_data AS nvarchar(max))
            FROM sys.dm_xe_sessions AS s
            JOIN sys.dm_xe_session_targets AS t ON t.event_session_address = s.address
            WHERE s.name = N'LinqToQueryPlan' AND t.target_name = N'ring_buffer';
            """;
        string? data;
        await using (var cmd = new SqlCommand(bufferSql, c))
        {
            data = (string?)await cmd.ExecuteScalarAsync();
        }
        if (string.IsNullOrEmpty(data)) return view;

        foreach (var e in XDocument.Parse(data).Root!.Elements("event"))
        {
            var name = (string?)e.Attribute("name") ?? "";
            var ts = DateTime.TryParse((string?)e.Attribute("timestamp"), out var t) ? t : DateTime.MinValue;
            string? Data(string n) => e.Elements("data").FirstOrDefault(d => (string?)d.Attribute("name") == n)?.Element("value")?.Value;
            string? Action(string n) => e.Elements("action").FirstOrDefault(d => (string?)d.Attribute("name") == n)?.Element("value")?.Value;

            var text = Data("statement") ?? Action("sql_text") ?? Data("batch_text") ?? "";
            if (text.Contains("sp_reset_connection", StringComparison.OrdinalIgnoreCase)) continue;
            if (tagFilter is not null && !text.Contains(tagFilter, StringComparison.Ordinal) &&
                !(Action("sql_text") ?? "").Contains(tagFilter, StringComparison.Ordinal))
            {
                continue;
            }

            if (name is "rpc_completed" or "sql_statement_completed" or "sql_batch_completed")
            {
                view.Statements.Add(new XeStatement(ts, name,
                    long.TryParse(Data("duration"), out var dur) ? dur / 1000.0 : 0,
                    long.TryParse(Data("logical_reads"), out var lr) ? lr : 0,
                    long.TryParse(Data("row_count"), out var rc) ? rc : 0,
                    text));
            }
            else
            {
                var fields = e.Elements("data")
                    .Select(d => $"{(string?)d.Attribute("name")} = {d.Element("text")?.Value ?? d.Element("value")?.Value}")
                    .ToList();
                view.OptimizerEvents.Add(new XeOptimizerEvent(ts, name, fields, Action("sql_text")));
            }
        }

        view.Statements.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        view.OptimizerEvents.Sort((a, b) => b.Timestamp.CompareTo(a.Timestamp));
        return view;
    }

    // ---------------------------------------------------------------------------------------------
    // Environment: everything the demos depend on, with a fix for each gap.
    // ---------------------------------------------------------------------------------------------
    // The page calls this on load; a stalled connection must come back as a message, not a spinner.
    public async Task<EnvironmentStatus> EnvironmentAsync()
    {
        var work = EnvironmentCoreAsync();
        if (await Task.WhenAny(work, Task.Delay(TimeSpan.FromSeconds(20))) == work) return await work;
        _ = work.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
        return new EnvironmentStatus
        {
            Server = options.ServerHost,
            Database = options.DatabaseName,
            ConnectionError = "No answer from SQL Server after 20 seconds. Run the connection check on the Lab page (or 'dotnet run --project src/LinqToQueryPlan.Demos -- doctor') to see which step stalls.",
        };
    }

    private async Task<EnvironmentStatus> EnvironmentCoreAsync()
    {
        var env = new EnvironmentStatus { Server = options.ServerHost, Database = options.DatabaseName };
        try
        {
            await using var c = await OpenAsync();
            const string sql = """
                SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(50)),
                       CAST(SERVERPROPERTY('ProductUpdateLevel') AS nvarchar(50)),
                       CAST(SERVERPROPERTY('Edition') AS nvarchar(100)),
                       (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()),
                       (SELECT actual_state_desc FROM sys.database_query_store_options),
                       (SELECT query_capture_mode_desc FROM sys.database_query_store_options),
                       CASE WHEN OBJECT_ID(N'dbo.Hits') IS NULL THEN -1 ELSE (SELECT SUM(rows) FROM sys.partitions WHERE object_id = OBJECT_ID(N'dbo.Hits') AND index_id IN (0, 1)) END,
                       CASE WHEN EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'LinqToQueryPlan') THEN 1 ELSE 0 END,
                       CASE WHEN EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE name = N'LinqToQueryPlan') THEN 1 ELSE 0 END;
                SELECT name, CAST(value AS nvarchar(20)) FROM sys.database_scoped_configurations
                WHERE name IN (N'PARAMETER_SENSITIVE_PLAN_OPTIMIZATION', N'OPTIONAL_PARAMETER_OPTIMIZATION', N'LAST_QUERY_PLAN_STATS',
                               N'OPTIMIZED_SP_EXECUTESQL', N'PARAMETER_SNIFFING');
                """;
            await using var cmd = new SqlCommand(sql, c);
            await using var r = await cmd.ExecuteReaderAsync();
            await r.ReadAsync();
            env.Connected = true;
            env.Version = $"{r.GetString(0)} {(r.IsDBNull(1) ? "" : r.GetString(1))}".Trim();
            env.Edition = r.GetString(2);
            env.CompatibilityLevel = r.GetByte(3);
            env.QueryStoreState = r.IsDBNull(4) ? "OFF" : r.GetString(4);
            env.QueryStoreCapture = r.IsDBNull(5) ? "" : r.GetString(5);
            var hits = Convert.ToInt64(r.GetValue(6));
            env.HitsRows = hits < 0 ? null : hits;
            env.XeSessionExists = r.GetInt32(7) == 1;
            env.XeSessionRunning = r.GetInt32(8) == 1;
            await r.NextResultAsync();
            while (await r.ReadAsync()) env.ScopedConfigs[r.GetString(0)] = r.GetString(1);
        }
        catch (Exception ex)
        {
            env.Connected = false;
            env.ConnectionError = ex.Message;
        }
        return env;
    }

    private async Task<SqlConnection> OpenAsync()
    {
        var c = new SqlConnection(Cs);
        await c.OpenAsync();
        return c;
    }
}

public sealed record PlanSnapshot(DateTime ServerTime, Dictionary<string, long> ExecutionCounts);

public sealed record PlanCacheGroup(string TagLine, int CachedPlans, int TotalUses, double TotalKb, string ObjTypes);

public sealed class QueryStoreView
{
    public List<QueryStoreQuery> Queries { get; } = [];
    public List<QueryStoreVariant> Variants { get; } = [];
    public List<string> Notes { get; } = [];
}

public sealed record QueryStoreQuery(long QueryId, long? VariantOf, int Plans, long Executions, DateTimeOffset? LastExecution, string? Hint, string SqlPreview, string Parameterization)
{
    // EF sends sp_executesql, which Query Store records as User parameterization. A pasted ToQueryString runs as an ad hoc
    // batch with local variables (None). Same query_hash, different query_id: put a hint on the wrong one and nothing changes.
    public string Role => VariantOf is { } parent ? $"PSP variant of {parent}"
        : Parameterization == "User" ? "your app (sp_executesql)"
        : "ad hoc batch (a query window)";
    public bool IsApp => VariantOf is null && Parameterization == "User";
}

public sealed record QueryStoreVariant(long ParentQueryId, long DispatcherPlanId, long VariantQueryId, long PlanId, string PlanType,
    long Executions, double AvgReads, double AvgMs);

public sealed class XeView
{
    public string Tag { get; set; } = "";
    public bool SessionExists { get; set; }
    public bool SessionRunning { get; set; }
    public List<XeStatement> Statements { get; } = [];
    public List<XeOptimizerEvent> OptimizerEvents { get; } = [];
}

public sealed record XeStatement(DateTime Timestamp, string Event, double DurationMs, long LogicalReads, long RowCount, string Text);

public sealed record XeOptimizerEvent(DateTime Timestamp, string Event, List<string> Fields, string? SqlText);

public sealed class EnvironmentStatus
{
    public string Server { get; init; } = "";
    public string Database { get; init; } = "";
    public bool Connected { get; set; }
    public string? ConnectionError { get; set; }
    public string Version { get; set; } = "";
    public string Edition { get; set; } = "";
    public int CompatibilityLevel { get; set; }
    public string QueryStoreState { get; set; } = "";
    public string QueryStoreCapture { get; set; } = "";
    public long? HitsRows { get; set; }
    public bool XeSessionExists { get; set; }
    public bool XeSessionRunning { get; set; }
    public Dictionary<string, string> ScopedConfigs { get; } = new(StringComparer.OrdinalIgnoreCase);

    public bool Is2025 => Version.StartsWith("17.", StringComparison.Ordinal);
    public bool Config(string name) => ScopedConfigs.TryGetValue(name, out var v) && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("ON", StringComparison.OrdinalIgnoreCase));

    public IEnumerable<EnvironmentCheck> Checks()
    {
        yield return new("SQL Server 2025", Is2025, Version, "OPPO and the 2025 PSP extensions need SQL Server 2025 (17.x).");
        yield return new("Compatibility level 170", CompatibilityLevel >= 170, CompatibilityLevel.ToString(), "Run setup (script 01). A restored backup keeps its old level.");
        yield return new("Query Store read-write", QueryStoreState == "READ_WRITE", QueryStoreState, "Run setup (script 01).");
        yield return new("Query Store captures ALL", QueryStoreCapture == "ALL", QueryStoreCapture, "AUTO skips cheap, infrequent queries, which is every demo query. Run setup.");
        yield return new("Last actual plans (LAST_QUERY_PLAN_STATS)", Config("LAST_QUERY_PLAN_STATS"), ScopedConfigs.GetValueOrDefault("LAST_QUERY_PLAN_STATS", "?"), "Without it the plan view shows estimates only. Run setup.");
        yield return new("PSP optimization", Config("PARAMETER_SENSITIVE_PLAN_OPTIMIZATION"), ScopedConfigs.GetValueOrDefault("PARAMETER_SENSITIVE_PLAN_OPTIMIZATION", "?"), "Off is expected while the case is set up (scripts/08). Setup or scripts/09 turns it back on.");
        yield return new("Optional parameter optimization", Config("OPTIONAL_PARAMETER_OPTIMIZATION") && CompatibilityLevel >= 170, ScopedConfigs.GetValueOrDefault("OPTIONAL_PARAMETER_OPTIMIZATION", "?"), "On, but only active at compat 170.");
        yield return new("XE session running", XeSessionRunning, XeSessionExists ? (XeSessionRunning ? "running" : "stopped") : "missing", "Run setup (script 03) or Reset, which restarts it.");
        yield return new("Hits table (optional)", HitsRows is > 0, HitsRows is null ? "not created" : $"{HitsRows:N0} rows", "Optional. Build it from the Lab page (script 06) for a stronger sniffing demo.", Optional: true);
    }
}

public sealed record EnvironmentCheck(string Name, bool Ok, string Value, string Fix, bool Optional = false);
