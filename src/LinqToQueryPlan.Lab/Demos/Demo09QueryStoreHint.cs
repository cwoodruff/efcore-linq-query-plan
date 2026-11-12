using LinqToQueryPlan.Lab.Model;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

// Act 3: case closed. The DBA-side fixes for the case from demo 1: a Query Store hint on the query_id (no redeploy),
// then the permanent fix, turning PSP back on. The application code never changes.
public static class Demo09QueryStoreHint
{
    private const string Tag = Demo01TheCase.Tag;

    public static DemoInfo Create() => new()
    {
        Number = 9,
        Slug = "query-store-hint",
        Title = "Case closed: the DBA fixes it with your query_id",
        TalkSection = "Act 3: Hand it off (case closed, ladder rung 3)",
        Proves = "Given a query_id, a DBA can change how your EF query is optimized without touching your code, and you can see it happen from the app side.",
        NotesFile = "09-query-store-hint.md",
        TagPrefix = Tag,
        Panels = ServerPanels.QueryStore,
        Actions =
        [
            new DemoAction
            {
                Key = "run",
                Label = "1. Run the case: small club, then the busy team",
                Description = "The demo 1 query, unchanged. Run it before the hint, after the hint, and after the permanent fix.",
                Inputs = [new("source", "Table", "auto", ["auto", "batting"])],
                Run = async (s, a) =>
                {
                    await using var db = s.CreateContext();
                    var src = await SkewedSource.ResolveAsync(db, a.Get("source", "auto"), s);
                    await Demo01TheCase.RunPair(s, db, src, rareFirst: true);
                    Demo01TheCase.AddComparison(s.Result);
                    await AddQueryStoreTable(s);
                    s.Result.DeferredXeTag = Tag;
                },
            },
            new DemoAction
            {
                Key = "find",
                Label = "2. Find the query_id",
                Description = "Query Store's statement text drops your tag, so this finds the query through the batch it was compiled from. This is the number a DBA works with.",
                Run = async (s, _) => await AddQueryStoreTable(s),
            },
            new DemoAction
            {
                Key = "apply",
                Label = "3. Apply a Query Store hint (as the DBA)",
                Description = "sys.sp_query_store_set_hints. No code change, no redeploy. Local container only.",
                RequiresAdmin = true,
                Inputs =
                [
                    new("queryId", "query_id", "", Help: "From step 2."),
                    new("hint", "Hint", "RECOMPILE", ["RECOMPILE", "OPTIMIZE FOR UNKNOWN", "USE HINT('DISABLE_PARAMETER_SNIFFING')", "USE HINT('DISABLE_PARAMETER_SENSITIVE_PLAN')"]),
                ],
                Run = async (s, a) =>
                {
                    var id = a.GetLong("queryId", 0);
                    if (id <= 0) throw new ArgumentException("Enter the query_id from step 2.");
                    var hint = a.Get("hint", "RECOMPILE");
                    await s.Admin.SetQueryStoreHintAsync(id, hint);
                    s.Result.Good($"Query Store hint OPTION ({hint}) set on query_id {id}. Now run step 1 again; the application code is exactly the same.");
                    await AddQueryStoreTable(s);
                },
            },
            new DemoAction
            {
                Key = "clear",
                Label = "4. Clear the hint",
                Description = "sys.sp_query_store_clear_hints. Local container only.",
                RequiresAdmin = true,
                Inputs = [new("queryId", "query_id", "")],
                Run = async (s, a) =>
                {
                    var id = a.GetLong("queryId", 0);
                    if (id <= 0) throw new ArgumentException("Enter the query_id from step 2.");
                    await s.Admin.ClearQueryStoreHintAsync(id);
                    s.Result.Good($"Hint cleared from query_id {id}.");
                    await AddQueryStoreTable(s);
                },
            },
            new DemoAction
            {
                Key = "psp",
                Label = "5. The permanent fix: turn PSP back on (as the DBA)",
                Description = "Runs scripts/09 (PSP on, this database's plans cleared), then runs the case again. In production the same fix is usually raising the compatibility level to 160 or 170. Local container only.",
                RequiresAdmin = true,
                Inputs = [new("source", "Table", "auto", ["auto", "batting"])],
                Run = async (s, a) =>
                {
                    var r = await s.Admin.CasePspOnAsync();
                    s.Result.Good($"PSP is back on and this database's plans are cleared ({r.ElapsedMs} ms). Running the case again:");
                    await using var db = s.CreateContext();
                    var src = await SkewedSource.ResolveAsync(db, a.Get("source", "auto"), s);
                    await Demo01TheCase.RunPair(s, db, src, rareFirst: true);
                    Demo01TheCase.AddComparison(s.Result);
                    if (s.Result.Queries.Any(q => q.Plans.Any(p => p.Parsed.PlanPerValueHint is not null)))
                        s.Result.Good("Each team got its own PSP variant. No hint, no RECOMPILE, and the query stays in the plan cache where monitoring can see it.");
                    else
                        s.Result.Warn("PSP didn't engage here. Check the Extended Events panel for parameter_sensitive_plan_optimization_skipped_reason; on a small table the skew threshold isn't met. The Query Store hint is the fix that still applies.");
                    await AddQueryStoreTable(s);
                    s.Result.DeferredXeTag = Tag;
                },
            },
        ],
    };

    private static async Task AddQueryStoreTable(LabSession s)
    {
        var qs = await s.Diagnostics.QueryStoreAsync(Tag);
        foreach (var n in qs.Notes) s.Result.Warn(n);
        var t = new TableSpec
        {
            Title = "Query Store: statements tagged demo:sniff",
            Columns = ["query_id", "role", "plans", "executions", "hint", "SQL"],
            Caption = "Put the hint on the row marked \"your app\". An ad hoc batch from a query window has the same query_hash but its own query_id, and PSP variants inherit the parent's hint.",
        };
        foreach (var q in qs.Queries)
            t.Rows.Add([$"{q.QueryId}", q.Role, $"{q.Plans}", $"{q.Executions}", q.Hint ?? "", Short(q.SqlPreview)]);
        s.Result.Tables.Add(t);
        if (qs.Queries.FirstOrDefault(q => q.IsApp) is { } app)
            s.Result.Good($"Your app's query is query_id {app.QueryId}. That's the number to hand the DBA.");
        if (qs.Queries.Any(q => !q.IsApp && q.VariantOf is null))
            s.Result.Info("There's also an ad hoc batch with the same query_hash: someone pasted ToQueryString into a query window. Query Store files it as a different query.");
        if (qs.Queries.Count == 0)
            s.Result.Warn("Nothing in Query Store yet. Run step 1 first. If it still doesn't show, check that Query Store capture mode is ALL (Lab page).");
        else if (qs.Queries.Any(q => q.Hint is not null))
            s.Result.Good("A hint is active. Run step 1 again and compare the plans: compiled per execution for RECOMPILE, one average plan for OPTIMIZE FOR UNKNOWN.");
    }

    private static string Short(string sql) => sql.Length > 140 ? sql[..140] + "..." : sql;
}
