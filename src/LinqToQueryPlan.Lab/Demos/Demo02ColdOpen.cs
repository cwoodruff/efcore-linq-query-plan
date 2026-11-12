using LinqToQueryPlan.Lab.Model;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

// Section 0. The old deck's Lahman query (World Series winners' pitchers) rewritten as LINQ.
public static class Demo02ColdOpen
{
    public static DemoInfo Create() => new()
    {
        Number = 2,
        Slug = "cold-open",
        Title = "Read a plan you didn't write: World Series pitchers",
        TalkSection = "Act 1: See it (reading a plan you didn't write)",
        Proves = "Ordinary-looking LINQ becomes a five-way join, and most of its cost lands on one operator you never wrote.",
        NotesFile = "02-cold-open.md",
        TagPrefix = "demo:coldopen",
        Panels = ServerPanels.PlanCache,
        Actions =
        [
            new DemoAction
            {
                Key = "run",
                Label = "Run the query",
                Description = "Pitchers on the World Series winner for a season, with their franchise name.",
                Inputs = [new("year", "Season", "2020", Help: "Any year with a World Series. 1904 and 1994 had none.")],
                Run = async (s, a) =>
                {
                    var year = a.GetShort("year", 2020);
                    var round = "WS";
                    await using var db = s.CreateContext();

                    var query =
                        (from sp in db.SeriesPost
                         join t in db.Teams on new { sp.YearId, L = sp.LgIdWinner, T = sp.TeamIdWinner } equals new { t.YearId, L = t.LgId, T = t.TeamId }
                         join f in db.TeamFranchises on t.FranchId equals f.FranchId
                         join p in db.Pitching on new { t.YearId, t.LgId, t.TeamId } equals new { p.YearId, p.LgId, p.TeamId }
                         join pp in db.People on p.PlayerId equals pp.PlayerId
                         where sp.Round == round && sp.YearId == year
                         select new { pp.NameFirst, pp.NameLast, Franchise = f.FranchName })
                        .Distinct()
                        .TagWith("demo:coldopen");

                    var (run, rows) = await s.RunAsync($"World Series {year} pitchers", query, "demo:coldopen");

                    var table = new TableSpec { Title = $"{rows.Count} pitchers", Columns = ["First", "Last", "Franchise"] };
                    foreach (var r in rows.Take(40)) table.Rows.Add([r.NameFirst ?? "", r.NameLast ?? "", r.Franchise ?? ""]);
                    s.Result.Tables.Add(table);

                    PlanNotes.Describe(s.Result, run);
                    s.Result.Info("Five tables, one statement, and EF wrote every join. Read the plan from the leaves up: data starts at the index operators and flows toward the root. Arrow width is row count.");
                },
            },
        ],
    };
}
