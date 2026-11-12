using LinqToQueryPlan.Lab.Model;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

// Section 5. Two sibling collection includes on Team: Batting and Pitching for one season.
public static class Demo06SplitVsSingle
{
    public static DemoInfo Create() => new()
    {
        Number = 6,
        Slug = "split-vs-single",
        Title = "Split vs single: changing the plan shape",
        TalkSection = "Act 2: Shape it",
        Proves = "Split query isn't faster by definition. It's a different plan shape (several small plans instead of one wide one) with a different consistency contract.",
        NotesFile = "06-split-vs-single.md",
        TagPrefix = "demo:split",
        Panels = ServerPanels.PlanCache,
        Actions =
        [
            new DemoAction
            {
                Key = "run",
                Label = "Run single, then split",
                Description = "Teams for a season with Include(Batting) and Include(Pitching), both ways.",
                Inputs = [new("year", "Season", "2024")],
                Run = async (s, a) =>
                {
                    var year = a.GetShort("year", 2024);
                    await using var db = s.CreateContext();

                    var single = db.Teams.TagWith("demo:split single")
                        .Where(t => t.YearId == year)
                        .Include(t => t.Batting)
                        .Include(t => t.Pitching)
                        .AsSingleQuery();
                    var (r1, teams) = await s.RunAsync("single query", single, "demo:split single");

                    var split = db.Teams.TagWith("demo:split split")
                        .Where(t => t.YearId == year)
                        .Include(t => t.Batting)
                        .Include(t => t.Pitching)
                        .AsSplitQuery();
                    var (r2, _) = await s.RunAsync("split query", split, "demo:split split");

                    var batters = teams.Sum(t => t.Batting.Count);
                    var pitchers = teams.Sum(t => t.Pitching.Count);
                    var cartesian = teams.Sum(t => (long)Math.Max(1, t.Batting.Count) * Math.Max(1, t.Pitching.Count));
                    var splitRows = teams.Count + batters + pitchers;

                    s.Result.Charts.Add(new ChartSpec
                    {
                        Title = "Rows the database had to send (log scale)",
                        Labels = ["single", "split"],
                        Series = [new("rows", [cartesian, splitRows])],
                        LogScale = true,
                        YTitle = "rows",
                    });
                    s.Result.Charts.Add(new ChartSpec
                    {
                        Title = "Elapsed ms",
                        Labels = ["single", "split"],
                        Series = [new("ms", [r1.ElapsedMs, r2.ElapsedMs])],
                        YTitle = "ms",
                    });

                    s.Result.Info($"{teams.Count} teams, {batters:N0} batting rows, {pitchers:N0} pitching rows.");
                    s.Result.Warn($"Single query: two collections at the same level multiply. The database produced about {cartesian:N0} rows to give you {splitRows:N0} rows of data.");
                    s.Result.Info($"Split query: {r2.Plans.Count} statements, {r2.Plans.Count} plans, {r2.Plans.Count} roundtrips. Each plan is a simple join; none of them is wide.");
                    if (s.Result.EfLog.Any(l => l.Contains("MultipleCollectionIncludeWarning", StringComparison.Ordinal) || l.Contains("single query", StringComparison.OrdinalIgnoreCase)))
                        s.Result.Info("EF logged a warning for the single query because you loaded two collections without choosing a splitting behavior. It's in the EF log below.");
                    s.Result.Info("EF 11 drops redundant to-one joins and ORDER BY keys from these statements. Same LINQ, smaller plans, no code change.");
                    PlanNotes.Describe(s.Result, r1);
                    PlanNotes.Describe(s.Result, r2);
                },
            },
        ],
    };
}
