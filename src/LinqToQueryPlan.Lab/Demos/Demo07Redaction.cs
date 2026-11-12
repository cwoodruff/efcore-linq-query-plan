using LinqToQueryPlan.Lab.Model;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

// Section 6. EF 10 redacts inlined constants from the log. The database still receives the literals.
public static class Demo07Redaction
{
    public const string Tag = "demo:redaction";

    public static DemoInfo Create() => new()
    {
        Number = 7,
        Slug = "redaction",
        Title = "What your log hides and your DBA sees",
        TalkSection = "Act 3: Hand it off",
        Proves = "The value you inlined with EF.Constant so the optimizer could see it is the value your own log now hides. The server, the plan cache, and the XE trace all still have it.",
        NotesFile = "07-redaction.md",
        TagPrefix = Tag,
        Panels = ServerPanels.ExtendedEvents | ServerPanels.PlanCache,
        Actions =
        [
            new DemoAction
            {
                Key = "run",
                Label = "Run with and without sensitive data logging",
                Description = "Counts players born in three countries, with the list inlined via EF.Constant.",
                Inputs = [new("countries", "Countries (comma separated)", "Cuba,Venezuela,Dominican Republic")],
                Run = async (s, a) =>
                {
                    var countries = a.Get("countries", "Cuba,Venezuela,Dominican Republic")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                    var defaultLog = new List<string>();
                    await using (var db = s.CreateContext(logSink: defaultLog))
                    {
                        await s.RunCountAsync("default logging",
                            db.People.TagWith(Tag).Where(p => EF.Constant(countries).Contains(p.BirthCountry!)), Tag);
                    }

                    var sensitiveLog = new List<string>();
                    await using (var db = s.CreateContext(c => c with { SensitiveDataLogging = true }, sensitiveLog))
                    {
                        await s.RunCountAsync("EnableSensitiveDataLogging",
                            db.People.TagWith(Tag).Where(p => EF.Constant(countries).Contains(p.BirthCountry!)), Tag);
                    }

                    var serverText = s.Result.Queries.SelectMany(q => q.Plans).Select(p => p.Parsed.StatementText).FirstOrDefault()
                                     ?? "(no cached plan found)";

                    s.Result.Panels.Add(new TextPanel { Title = "Your log, default settings", Text = Last(defaultLog), Language = "text", Group = "wall", Badge = "redacted" });
                    s.Result.Panels.Add(new TextPanel { Title = "Your log, EnableSensitiveDataLogging", Text = Last(sensitiveLog), Language = "text", Group = "wall", Badge = "values" });
                    s.Result.Panels.Add(new TextPanel { Title = "What the server ran (plan cache)", Text = serverText, Group = "wall", Badge = "server" });

                    s.Result.Good($"{s.Result.Queries[0].Rows:N0} players. Same SQL sent both times.");
                    s.Result.Warn("EF 10 replaced the inlined values with ? in the default log. EF never logged parameter values by default; what's new is that it also hides values it inlined into the SQL text.");
                    s.Result.Info("The plan cache and the XE trace below show the real values. When you debug this with a DBA, they're looking at the right-hand panel and you're looking at the left.");
                    s.Result.DeferredXeTag = Tag;
                },
            },
        ],
    };

    private static string Last(List<string> log) => log.LastOrDefault(l => l.Contains("SELECT", StringComparison.OrdinalIgnoreCase)) ?? "(nothing logged)";
}
