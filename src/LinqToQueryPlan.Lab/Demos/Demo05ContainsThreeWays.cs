using LinqToQueryPlan.Lab.Model;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

// Section 4. The same Contains() query under the three ParameterTranslationMode values.
//
//   Constant            IN ('DET', 'CHN', ...)            one plan per distinct list     (EF 7 and earlier)
//   Parameter           IN (SELECT value FROM OPENJSON)   one plan, no cardinality        (EF 8 and 9 default)
//   MultipleParameters  IN (@teamIds1, @teamIds2, ...)    one plan per padding bucket     (EF 10 default)
public static class Demo05ContainsThreeWays
{
    private static readonly string[] AllTeams =
    [
        "DET","CHN","NYA","BOS","SLN","PHI","PIT","CIN","CLE","CHA","BAL","SFN","LAN","MIN","OAK","KCA","TEX","HOU","ATL","MIL",
        "SEA","TOR","SDN","NYN","COL","FLO","ARI","TBA","WAS","ANA","MON","SLA","BRO","WS1","ML1","KC1","SE1","PHA","BSN","NY1",
        "BLA","WSH","PRO","LS3","CL4","TRO","WOR","BFN","HAR","SL4","SYR","IND","MLA","RC1","KEO","NH1","BR2","ELI","PH2","CN1",
    ];

    private static readonly int[] Sizes = [3, 8, 25, 60];

    public static DemoInfo Create() => new()
    {
        Number = 5,
        Slug = "contains",
        Title = "Contains: three translations, three plan cache behaviors",
        TalkSection = "Act 2: Shape it",
        Proves = "One LINQ line has had three SQL shapes since EF 7, and each one trades plan cache size against what the optimizer knows about the list.",
        NotesFile = "05-contains.md",
        TagPrefix = "demo:contains",
        Panels = ServerPanels.PlanCache,
        Actions =
        [
            new DemoAction
            {
                Key = "run-all",
                Label = "Run all three modes",
                Description = "Two different lists each of 3, 8, 25, and 60 team IDs under each mode (24 queries). Plans are captured for the first 8-item list.",
                Inputs = [new("year", "Season", "1984")],
                Run = async (s, a) =>
                {
                    var year = a.GetShort("year", 1984);
                    var modes = new[] { ParameterTranslationMode.Constant, ParameterTranslationMode.Parameter, ParameterTranslationMode.MultipleParameters };
                    var timings = new Dictionary<ParameterTranslationMode, List<double>>();

                    foreach (var mode in modes)
                    {
                        await using var db = s.CreateContext(c => c with { CollectionMode = mode }, logSink: []);
                        timings[mode] = [];
                        foreach (var size in Sizes)
                        {
                            // Two different lists of each size: same shape, different values.
                            // Constant mode caches a plan per list; the other modes don't care which values are in it.
                            foreach (var offset in new[] { 0, 1 })
                            {
                                var teamIds = AllTeams.Skip(offset).Take(size).ToArray();
                                // Copy into this scope: EF names parameters after the closure field, and a variable captured
                                // through nested lambdas comes out as @_8__locals2_8__locals1_year.
                                var season = year;
                                var tag = $"demo:contains mode={mode}";
                                var query = db.Batting
                                    .TagWith(tag)
                                    .Where(b => b.YearId == season && teamIds.Contains(b.TeamId))
                                    .Select(b => new { b.TeamId, b.PlayerId, b.H });

                                var first = offset == 0;
                                var (run, _) = await s.RunAsync($"{mode}, {size} teams{(first ? "" : " (second list)")}", query, tag, capturePlan: first && size == 8);
                                if (first) timings[mode].Add(run.ElapsedMs);
                                if (first && size == 8)
                                    s.Result.Panels.Add(new TextPanel { Title = $"{mode} (8 values)", Text = run.Sql ?? "", Group = "sql8" });
                            }
                        }
                    }

                    // keep only the 8-item runs in the query list so the plan views stay readable
                    s.Result.Queries.RemoveAll(q => !q.Label.EndsWith(", 8 teams", StringComparison.Ordinal));

                    s.Result.Charts.Add(new ChartSpec
                    {
                        Title = "Elapsed ms by list size and mode",
                        Labels = Sizes.Select(x => $"{x} teams").ToList(),
                        Series = modes.Select(m => new ChartSeries(m.ToString(), timings[m])).ToList(),
                        YTitle = "ms",
                    });

                    var cache = await s.Diagnostics.PlanCacheByTagAsync("demo:contains");
                    int Plans(string mode) => cache.Where(c => c.TagLine.EndsWith("mode=" + mode, StringComparison.Ordinal)).Sum(c => c.CachedPlans);
                    s.Result.Charts.Add(new ChartSpec
                    {
                        // DataStory rule: the title states the finding, not the axis.
                        Title = $"Constant cached {Plans("Constant")} plans, Parameter (OPENJSON) cached {Plans("Parameter")}, MultipleParameters cached {Plans("MultipleParameters")}",
                        Labels = cache.Select(c => c.TagLine.Replace("demo:contains ", "")).ToList(),
                        Series = [new("cached plans", cache.Select(c => (double)c.CachedPlans).ToList())],
                        YTitle = "plans",
                    });

                    var openJson = s.Result.Queries
                        .SelectMany(q => q.Plans)
                        .SelectMany(p => p.Parsed.AllNodes())
                        .FirstOrDefault(n => n.PhysicalOp.Contains("Table-valued function", StringComparison.OrdinalIgnoreCase) ||
                                             (n.ObjectName?.Contains("OPENJSON", StringComparison.OrdinalIgnoreCase) ?? false));
                    if (openJson is not null)
                        s.Result.Warn($"Parameter mode: the optimizer estimated {PlanNotes.Fmt(openJson.EstRowsTotal)} rows out of OPENJSON for a list that had 8. It can't see inside the JSON string.");

                    s.Result.Info("Look at the MultipleParameters SQL for 8 values: EF pads to 10 parameters (the last values repeat) so lists of 6 to 10 share one plan.");
                    s.Result.Info("Expected counts after a reset: Constant 8 (one per distinct list), Parameter 1 (one for everything), MultipleParameters 4 (one per padding bucket, shared by both lists of a size).");
                    foreach (var q in s.Result.Queries) PlanNotes.Describe(s.Result, q);
                },
            },
            new DemoAction
            {
                Key = "reset",
                Label = "Reset plan cache",
                Description = "Runs scripts/05 so the plan counts start from zero. Local container only.",
                RequiresAdmin = true,
                Run = async (s, _) =>
                {
                    var r = await s.Admin.ResetAsync();
                    s.Result.Good($"Plan cache cleared ({r.ElapsedMs} ms). Run all three modes again for clean counts.");
                },
            },
        ],
    };
}
