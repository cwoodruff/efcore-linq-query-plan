using LinqToQueryPlan.Lab.Model;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

// The case that runs through the whole talk: one team page, instant for a tiny club, slow for the busiest team.
// Opens the talk (the ticket and the false clue), gets cracked in Act 1 (compiled vs runtime value),
// and is closed in Act 3 by demo 9 (Query Store hint, then turning PSP back on).
public static class Demo01TheCase
{
    public const string Tag = "demo:sniff";

    private static readonly DemoInput SourceInput = new("source", "Table", "auto", ["auto", "batting"],
        "auto uses Hits when it exists, otherwise Batting.teamID.");

    public static DemoInfo Create() => new()
    {
        Number = 1,
        Slug = "sniffing",
        Title = "The case: the slow team page",
        TalkSection = "The case: opens the talk, cracked in Act 1, closed in Act 3",
        Proves = "The first value compiles the plan and every later value inherits it, and the usual way of checking (pasting ToQueryString into a query window) shows you a different plan from the one your app ran.",
        NotesFile = "01-sniffing.md",
        TagPrefix = Tag,
        Panels = ServerPanels.PlanCache | ServerPanels.QueryStore | ServerPanels.ExtendedEvents,
        Actions =
        [
            new DemoAction
            {
                Key = "setup",
                Label = "0. Set up the case",
                Description = "Runs scripts/08: turns PSP off (so this database behaves like one below compat 160) and clears this database's plans and Query Store. Run it before the talk. Local container only.",
                RequiresAdmin = true,
                Run = async (s, _) =>
                {
                    var r = await s.Admin.CaseSetupAsync();
                    s.Result.Good($"Case is set: PSP off, plans and Query Store cleared ({r.Batches} batches, {r.ElapsedMs} ms). The next value to run compiles the plan.");
                    s.Result.Info("Demo 9 turns PSP back on at the end. Setup on the Lab page does too.");
                },
            },
            new DemoAction
            {
                Key = "ticket",
                Label = "1. The ticket: small club, then the busiest team",
                Description = "The same LINQ for two teams. The small club compiles the plan; the busy team inherits it.",
                Inputs = [SourceInput],
                Run = async (s, a) =>
                {
                    await using var db = s.CreateContext();
                    var src = await SkewedSource.ResolveAsync(db, a.Get("source", "auto"), s);
                    if (src.Notice is not null) s.Result.Warn(src.Notice);
                    s.Result.Info(src.Describe());
                    await RunPair(s, db, src, rareFirst: true);
                    AddComparison(s.Result);
                    AddVerdict(s.Result);
                    s.Result.DeferredXeTag = Tag;
                },
            },
            new DemoAction
            {
                Key = "clue",
                Label = "2. The false clue: paste ToQueryString into a query window",
                Description = "Runs the busy team's query the way the app does, then runs its ToQueryString() output as an ad hoc batch, the way you would in VS Code. Same SQL, two plans.",
                Inputs = [SourceInput],
                Run = async (s, a) =>
                {
                    await using var db = s.CreateContext();
                    var src = await SkewedSource.ResolveAsync(db, a.Get("source", "auto"), s);
                    if (src.Notice is not null) s.Result.Warn(src.Notice);

                    // The app's side: make sure the cached plan is the small club's, then run the busy team through it.
                    var (rareRun, _) = await s.RunAsync($"app: small club '{src.RareKey}'", src.Query(db, src.RareKey).TagWith(Tag), Tag);
                    var appQuery = src.Query(db, src.CommonKey).TagWith(Tag);
                    var (appRun, _) = await s.RunAsync($"app: busy team '{src.CommonKey}'", appQuery, Tag);

                    // The developer's side: ToQueryString, pasted into a query window and run as is.
                    var pasted = appQuery.ToQueryString();
                    var pasteRun = await s.RunSqlTextAsync($"query window: pasted ToQueryString for '{src.CommonKey}'", pasted, Tag);

                    s.Result.Queries.Remove(rareRun);
                    // The pasted batch is two statements (DECLARE, then SELECT). Keep the SELECT's plan.
                    if (pasteRun.Plans.Any(p => p.Parsed.NodeCount > 0)) pasteRun.Plans.RemoveAll(p => p.Parsed.NodeCount == 0);
                    var app = appRun.Plans.LastOrDefault();
                    var win = pasteRun.Plans.OrderByDescending(p => p.Parsed.NodeCount).FirstOrDefault();
                    var compiled = app?.Parsed.Parameters.FirstOrDefault()?.CompiledValue;
                    if (compiled is not null && Unquote(compiled) == src.CommonKey)
                        s.Result.Warn("The app's cached plan was compiled for the busy team, so there's no sniffing to see. Run \"0. Set up the case\" and try again.");

                    var t = new TableSpec
                    {
                        Title = "Same SQL text, two plans",
                        Columns = ["Where it ran", "Plan", "Estimated rows", "Actual rows", "Reads", "ms", "Built for"],
                        Caption = "The query window's plan is built from the column's average (a local variable isn't sniffed). The app's plan was built for whichever team ran first.",
                    };
                    t.Rows.Add(["Your app (sp_executesql)", Shape(app), Est(app), $"{appRun.Rows:N0}", $"{app?.LastLogicalReads:N0}", $"{appRun.ElapsedMs:N0}", compiled ?? "?"]);
                    t.Rows.Add(["Query window (ToQueryString paste)", Shape(win), Est(win), $"{pasteRun.Rows:N0}", $"{win?.LastLogicalReads:N0}", $"{pasteRun.ElapsedMs:N0}", "the column average"]);
                    s.Result.Tables.Add(t);

                    s.Result.Charts.Add(new ChartSpec
                    {
                        Title = "Same SQL, two plans: elapsed ms and logical reads (log scale)",
                        Labels = ["your app", "query window"],
                        Series =
                        [
                            new("elapsed ms", [(double)appRun.ElapsedMs, (double)pasteRun.ElapsedMs]),
                            new("logical reads", [(double)(app?.LastLogicalReads ?? 0), (double)(win?.LastLogicalReads ?? 0)]),
                        ],
                        LogScale = true,
                    });

                    if (app is not null && win is not null && Shape(app) != Shape(win))
                        s.Result.Warn($"Different plans for the same SQL. The window ran a {Shape(win)} with {win.LastLogicalReads:N0} reads; the app ran a {Shape(app)} with {app.LastLogicalReads:N0} reads. Tune the window's plan and you've tuned a plan your app never runs.");
                    else
                        s.Result.Info("Same plan shape this time, but compare the estimated rows: the window estimates from the column's average, the app from the first value it saw. On other data the shapes split.");
                    s.Result.Info("To see the app's real plan, read it from the server: sys.dm_exec_query_plan_stats with LAST_QUERY_PLAN_STATS on. That's what this lab does for every query.");
                },
            },
            new DemoAction
            {
                Key = "skew",
                Label = "Look at the skew",
                Description = "Rows per key for the most and least common values. No plan capture, just the data shape.",
                Inputs = [SourceInput],
                Run = async (s, a) =>
                {
                    await using var db = s.CreateContext();
                    var src = await SkewedSource.ResolveAsync(db, a.Get("source", "auto"), s);
                    if (src.Notice is not null) s.Result.Warn(src.Notice);
                    s.Result.Info(src.Describe());
                    var ratio = src.CommonCount / (double)Math.Max(1, src.RareCount);
                    s.Result.Charts.Add(new ChartSpec
                    {
                        Title = $"'{src.CommonKey}' has {ratio:N0}x the rows of '{src.RareKey}'. One plan can't fit both.",
                        Labels = [.. src.Head.Select(x => x.Key), .. src.Tail.Select(x => x.Key)],
                        Series = [new("rows", [.. src.Head.Select(x => (double)x.Count), .. src.Tail.Select(x => (double)x.Count)])],
                        LogScale = true,
                        YTitle = "rows",
                    });
                },
            },
            new DemoAction
            {
                Key = "common-then-rare",
                Label = "Bonus: busy team first, then the small club",
                Description = "Run \"0. Set up the case\" first. Now the small club inherits a plan built for hundreds of thousands of rows.",
                Inputs = [SourceInput],
                Run = async (s, a) =>
                {
                    await using var db = s.CreateContext();
                    var src = await SkewedSource.ResolveAsync(db, a.Get("source", "auto"), s);
                    if (src.Notice is not null) s.Result.Warn(src.Notice);
                    await RunPair(s, db, src, rareFirst: false);
                    AddComparison(s.Result);
                    AddVerdict(s.Result);
                    s.Result.DeferredXeTag = Tag;
                },
            },
        ],
    };

    public static async Task RunPair(LabSession s, LinqToQueryPlan.Data.LahmanContext db, SkewedSource src, bool rareFirst)
    {
        var order = rareFirst
            ? new[] { ("small club", src.RareKey), ("busy team", src.CommonKey) }
            : new[] { ("busy team", src.CommonKey), ("small club", src.RareKey) };
        foreach (var (label, k) in order)
        {
            var (run, _) = await s.RunAsync($"{label} '{k}'", src.Query(db, k).TagWith(Tag), Tag);
            PlanNotes.Describe(s.Result, run);
        }
    }

    private static void AddVerdict(RunResult result)
    {
        var last = result.Queries.LastOrDefault()?.Plans.LastOrDefault();
        if (last is null) return;
        if (last.Parsed.PlanPerValueHint is not null)
            result.Good("PSP is on: each team got a variant built for its own row count. To see the case as most production databases see it, run \"0. Set up the case\" first.");
        else if (!last.CompiledForThisRun)
            result.Warn("The second team reused the first team's plan. Compare \"Compiled for\" with the key that ran: that one line of plan XML is the whole diagnosis.");
    }

    private static string Unquote(string v)
    {
        v = SqlText.Normalize(v);
        if (v.StartsWith("N'", StringComparison.Ordinal)) v = v[1..];
        return v.Trim('\'');
    }

    private static string Shape(CapturedPlan? p)
    {
        if (p is null) return "no plan captured";
        var nodes = p.Parsed.AllNodes().ToList();
        var lookup = nodes.Any(n => n.PhysicalOp.Contains("Lookup", StringComparison.OrdinalIgnoreCase) || (n.LogicalOp.Contains("Lookup", StringComparison.OrdinalIgnoreCase)));
        var seek = nodes.Any(n => n.PhysicalOp.Contains("Seek", StringComparison.OrdinalIgnoreCase));
        var scan = nodes.Any(n => n.PhysicalOp.Contains("Scan", StringComparison.OrdinalIgnoreCase));
        if (seek && (lookup || nodes.Count(n => n.PhysicalOp.Contains("Seek", StringComparison.OrdinalIgnoreCase)) > 1)) return "seek + lookup per row";
        if (scan) return "scan";
        if (seek) return "seek";
        return p.Parsed.Root?.PhysicalOp ?? "?";
    }

    private static string Est(CapturedPlan? p)
    {
        var leaf = p?.Parsed.AllNodes().FirstOrDefault(n => n.PhysicalOp.Contains("Seek", StringComparison.OrdinalIgnoreCase) || n.PhysicalOp.Contains("Scan", StringComparison.OrdinalIgnoreCase));
        return leaf is null ? "" : PlanNotes.Fmt(leaf.EstRowsTotal);
    }

    public static void AddComparison(RunResult result)
    {
        var runs = result.Queries;
        result.Charts.Add(new ChartSpec
        {
            Title = "Elapsed ms and logical reads per run",
            Labels = runs.Select(r => r.Label).ToList(),
            Series =
            [
                new("elapsed ms", runs.Select(r => (double)r.ElapsedMs).ToList()),
                new("logical reads", runs.Select(r => (double)(r.Plans.LastOrDefault()?.LastLogicalReads ?? 0)).ToList()),
            ],
            LogScale = true,
        });

        var t = new TableSpec
        {
            Title = "What each run got",
            Columns = ["Run", "Rows", "ms", "Plan", "Compiled for", "Root est rows", "Root actual rows", "Reads"],
        };
        foreach (var r in runs)
        {
            var p = r.Plans.LastOrDefault();
            var compiled = p?.Parsed.Parameters.FirstOrDefault()?.CompiledValue ?? "";
            var est = p?.Parsed.Root?.EstRowsTotal;
            t.Rows.Add([
                r.Label, $"{r.Rows:N0}", $"{r.ElapsedMs}",
                p is null ? "none" : (p.CompiledForThisRun ? "new" : "reused") + (p.Parsed.PlanPerValueHint is null ? "" : " (PSP variant)"),
                compiled,
                est is null ? "" : PlanNotes.Fmt(est.Value),
                p?.Parsed.Root?.ActualRows is { } act ? PlanNotes.Fmt(act) : $"{p?.LastRows:N0}",
                $"{p?.LastLogicalReads:N0}",
            ]);
        }
        result.Tables.Add(t);
    }
}
