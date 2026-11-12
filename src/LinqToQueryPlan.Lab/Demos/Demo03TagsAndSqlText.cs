using LinqToQueryPlan.Lab.Model;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

// Section 2. Correlating C# to what the DBA sees: TagWith, TagWithCallSite, and EF 10's simplified parameter names.
public static class Demo03TagsAndSqlText
{
    public static DemoInfo Create() => new()
    {
        Number = 3,
        Slug = "tags",
        Title = "Tags, call sites, and readable parameter names",
        TalkSection = "Act 1: See it",
        Proves = "A tag and a call site turn an anonymous statement in the plan cache into a line of C# you can find, and give your DBA a query_id to talk about.",
        NotesFile = "03-tags.md",
        TagPrefix = "demo:tags",
        Panels = ServerPanels.PlanCache | ServerPanels.QueryStore,
        Actions =
        [
            new DemoAction
            {
                Key = "run",
                Label = "Run the tagged query",
                Description = "Batting lines for one team-season, tagged with a description and the call site.",
                Inputs =
                [
                    new("teamId", "Team ID", "DET"),
                    new("year", "Season", "1984"),
                ],
                Run = async (s, a) =>
                {
                    var teamId = a.Get("teamId", "DET");
                    var yearId = a.GetShort("year", 1984);
                    await using var db = s.CreateContext();

                    var query = db.Batting
                        .TagWith("demo:tags")
                        .TagWith("Roster batting lines for one team-season")
                        .TagWithCallSite()
                        .Where(b => b.TeamId == teamId && b.YearId == yearId)
                        .OrderByDescending(b => b.Hr)
                        .Select(b => new { b.Player.NameFirst, b.Player.NameLast, b.Hr, b.Rbi });

                    var (run, rows) = await s.RunAsync($"{teamId} {yearId} roster", query, "demo:tags");

                    var table = new TableSpec { Title = $"{rows.Count} batters", Columns = ["First", "Last", "HR", "RBI"] };
                    foreach (var r in rows.Take(30)) table.Rows.Add([r.NameFirst ?? "", r.NameLast ?? "", $"{r.Hr}", $"{r.Rbi}"]);
                    s.Result.Tables.Add(table);

                    var plan = run.Plans.LastOrDefault();
                    if (plan is not null)
                    {
                        var pt = new TableSpec { Title = "Parameters as the server saw them", Columns = ["Name", "Type", "Compiled value"] };
                        foreach (var p in plan.Parsed.Parameters) pt.Rows.Add([p.Name, p.DataType ?? "", p.CompiledValue ?? ""]);
                        s.Result.Tables.Add(pt);
                    }

                    s.Result.Good("EF 10 names parameters after your variables (@teamId, @yearId) instead of @__teamId_0. Plan XML and Query Store are readable now.");
                    s.Result.Info("Parameter types match the columns (nvarchar(4), smallint) because the model declares HasMaxLength. A mismatch here causes implicit conversions in the plan.");
                    s.Result.Info("Open the Query Store panel below: the query_id is the number you hand a DBA.");
                    PlanNotes.Describe(s.Result, run);
                },
            },
        ],
    };
}
