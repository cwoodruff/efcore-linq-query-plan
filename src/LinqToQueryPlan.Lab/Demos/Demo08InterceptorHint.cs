using LinqToQueryPlan.Lab.Model;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

// Section 7, rung 2. The sniffing query from demo 1 with a hint injected by TagHintInterceptor.
public static class Demo08InterceptorHint
{
    public const string Tag = "demo:hint";

    public static DemoInfo Create() => new()
    {
        Number = 8,
        Slug = "interceptor-hint",
        Title = "Interceptor-injected query hints",
        TalkSection = "Act 3: Hand it off (ladder, rung 2)",
        Proves = "EF has no hint API, but a tag plus a DbCommandInterceptor puts OPTION (...) on exactly the queries you choose, and the cost of each hint is visible in the plan cache.",
        NotesFile = "08-interceptor-hint.md",
        TagPrefix = Tag,
        Panels = ServerPanels.PlanCache | ServerPanels.QueryStore,
        Actions =
        [
            new DemoAction
            {
                Key = "run",
                Label = "Run rare and common keys with a hint",
                Description = "The demo 1 query, tagged hint:<your choice> so the interceptor appends it.",
                Inputs =
                [
                    new("hint", "Hint", "RECOMPILE", ["RECOMPILE", "OPTIMIZE FOR UNKNOWN", "USE HINT('DISABLE_PARAMETER_SNIFFING')"]),
                    new("source", "Table", "auto", ["auto", "batting"]),
                ],
                Run = async (s, a) =>
                {
                    var hint = a.Get("hint", "RECOMPILE");
                    await using var db = s.CreateContext(c => c with { EnableHintInterceptor = true });
                    var src = await SkewedSource.ResolveAsync(db, a.Get("source", "auto"), s);
                    if (src.Notice is not null) s.Result.Warn(src.Notice);

                    foreach (var (label, key) in new[] { ("rare", src.RareKey), ("common", src.CommonKey) })
                    {
                        // The hint tag must come first: the interceptor reads only the first comment line.
                        var query = src.Query(db, key).TagWith($"hint:{hint}").TagWith(Tag);
                        var (run, _) = await s.RunAsync($"{label} key '{key}' + OPTION ({hint})", query, Tag);
                        PlanNotes.Describe(s.Result, run);
                    }

                    var ran = s.Result.Queries.SelectMany(q => q.Plans).Select(p => p.Parsed.StatementText).LastOrDefault();
                    if (ran is not null)
                        s.Result.Panels.Add(new TextPanel { Title = "Statement text in the plan cache (note the OPTION clause)", Text = ran });

                    s.Result.Info(hint switch
                    {
                        "RECOMPILE" => "RECOMPILE: a fresh plan per execution, built for that execution's value. You pay compile CPU every time, and PSP optimization is switched off for this statement.",
                        "OPTIMIZE FOR UNKNOWN" => "OPTIMIZE FOR UNKNOWN: one plan built from the average density. Same plan for rare and common keys, right for neither extreme.",
                        _ => "DISABLE_PARAMETER_SNIFFING: same effect as OPTIMIZE FOR UNKNOWN, and it also disables PSP optimization.",
                    });
                    Demo01TheCase.AddComparison(s.Result);
                    s.Result.DeferredXeTag = Tag;
                },
            },
        ],
    };
}
