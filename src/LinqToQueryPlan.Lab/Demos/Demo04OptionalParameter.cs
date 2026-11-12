using System.Text.RegularExpressions;
using LinqToQueryPlan.Lab.Model;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

// Section 3, the experiment. OPPO (SQL Server 2025, compat 170) was built for  WHERE (col = @p OR @p IS NULL).
// The LINQ equivalent is  .Where(x => key == null || x.Col == key).  Does that shape ever reach the optimizer?
public static partial class Demo04OptionalParameter
{
    public const string Tag = "demo:optional";

    public static DemoInfo Create() => new()
    {
        Number = 4,
        Slug = "optional-parameter",
        Title = "Optional parameter: who handles it, EF or OPPO?",
        TalkSection = "Act 2: Shape it (the OPPO experiment)",
        Proves = "Whether EF sends the optional-parameter pattern SQL Server 2025 optimizes for, or quietly removes it before it ever crosses the wall.",
        NotesFile = "04-optional-parameter.md",
        TagPrefix = Tag,
        Panels = ServerPanels.ExtendedEvents | ServerPanels.PlanCache,
        Actions =
        [
            new DemoAction
            {
                Key = "run",
                Label = "Run with key = null, then key = common",
                Description = "Same LINQ, two parameter values. Compares the SQL EF sent each time.",
                Inputs = [new("source", "Table", "auto", ["auto", "batting"])],
                Run = async (s, a) =>
                {
                    await using var db = s.CreateContext();
                    var src = await SkewedSource.ResolveAsync(db, a.Get("source", "auto"), s);
                    if (src.Notice is not null) s.Result.Warn(src.Notice);

                    var texts = new List<string>();
                    foreach (var key in new string?[] { null, src.CommonKey })
                    {
                        var run = await s.RunCountAsync(key is null ? "key = null" : $"key = '{key}'", src.OptionalQuery(db, key).TagWith(Tag), Tag);
                        var server = run.Plans.LastOrDefault()?.Parsed.StatementText ?? run.Sql ?? "";
                        texts.Add(server);
                        s.Result.Panels.Add(new TextPanel { Title = run.Label + " (statement the server ran)", Text = server, Group = "compare" });
                        PlanNotes.Describe(s.Result, run);
                    }

                    var identical = Normalize(texts[0]) == Normalize(texts[1]);
                    if (identical)
                    {
                        s.Result.Good("Identical SQL for null and non-null: the (col = @key OR @key IS NULL) shape reached the server, so OPPO is the one choosing between a scan and a seek. Check the XE panel for query_with_optional_parameter_predicate.");
                    }
                    else
                    {
                        s.Result.Warn("Different SQL for null and non-null. EF looked at the parameter's value on the client, produced one statement per case, and cached both. OPPO never sees the optional-parameter pattern from this LINQ.");
                        s.Result.Info("That's not bad news: two statements get two plans, which is what OPPO would have built for you. It does mean the DBA sees two queries where you wrote one.");
                    }
                    s.Result.DeferredXeTag = Tag;
                },
            },
        ],
    };

    private static string Normalize(string sql) => Whitespace().Replace(sql, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
