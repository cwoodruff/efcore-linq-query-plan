using LinqToQueryPlan.Lab;
using LinqToQueryPlan.Lab.Demos;
using LinqToQueryPlan.Lab.Diagnostics;
using LinqToQueryPlan.Lab.Model;

// Terminal fallback for the web app. Same demos, same code (LinqToQueryPlan.Lab), printed as text.
//
//   dotnet run --project src/LinqToQueryPlan.Demos -- doctor             step-by-step connection check
//   dotnet run --project src/LinqToQueryPlan.Demos                      list demos and actions
//   dotnet run --project src/LinqToQueryPlan.Demos -- 3 rare-then-common  run one action
//   dotnet run --project src/LinqToQueryPlan.Demos -- 4 run-all year=1990 pass inputs as name=value
//
// Connection string: LAHMAN_CONNECTION environment variable. The app name is set for you.

var options = new LabOptions
{
    ConnectionString = Environment.GetEnvironmentVariable("LAHMAN_CONNECTION") ?? new LabOptions().ConnectionString,
    AllowAdminActions = Environment.GetEnvironmentVariable("LAB_ALLOW_ADMIN") == "1",
};
var scripts = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../scripts"));
var diagnostics = new ServerDiagnostics(options);
var admin = new LabAdmin(options, Directory.Exists(scripts) ? scripts : Path.GetFullPath("scripts"));

if (args.Length > 0 && args[0] == "doctor")
{
    var report = await new ConnectionDoctor(options).RunAsync();
    foreach (var step in report.Steps)
        Console.WriteLine($"{(step.Ok ? "ok  " : "FAIL")}  {step.Name,-34} {step.Ms,6} ms  {step.Detail}");
    Console.WriteLine();
    foreach (var a in report.Advice) Console.WriteLine("> " + a);
    return;
}

if (args.Length > 0 && args[0] is "setup" or "reset" or "inflate")
{
    var script = args[0] switch { "setup" => admin.SetupAsync(), "reset" => admin.ResetAsync(), _ => admin.InflateAsync() };
    try
    {
        var sr = await script;
        Console.WriteLine($"{sr.Name}: {sr.Batches} batches in {sr.ElapsedMs} ms");
        foreach (var m in sr.Messages) Console.WriteLine("  " + m);
        foreach (var rs in sr.ResultSets)
            foreach (var row in rs) Console.WriteLine("  " + string.Join(" | ", row));
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR: {ex.GetBaseException().Message}");
    }
    return;
}

if (args.Length == 0 || DemoCatalog.Find(args[0]) is not { } demo)
{
    Console.WriteLine("From LINQ to Query Plan: demos (the web app in src/LinqToQueryPlan.Web is the better way to run these)");
    Console.WriteLine();
    foreach (var d in DemoCatalog.All)
    {
        Console.WriteLine($"  {d.Number}  {d.Title}");
        foreach (var a in d.Actions)
        {
            var inputs = a.Inputs.Count == 0 ? "" : "  [" + string.Join(" ", a.Inputs.Select(i => $"{i.Name}={i.Default}")) + "]";
            Console.WriteLine($"       {a.Key,-18} {a.Label}{(a.RequiresAdmin ? " (admin: set LAB_ALLOW_ADMIN=1)" : "")}{inputs}");
        }
    }
    return;
}

var actionKey = args.Length > 1 && !args[1].Contains('=') ? args[1] : demo.Actions[0].Key;
var action = demo.Actions.FirstOrDefault(a => a.Key.Equals(actionKey, StringComparison.OrdinalIgnoreCase));
if (action is null)
{
    Console.WriteLine($"Demo {demo.Number} has no action '{actionKey}'. Actions: {string.Join(", ", demo.Actions.Select(a => a.Key))}");
    return;
}

var values = args.Skip(1).Where(x => x.Contains('='))
    .Select(x => x.Split('=', 2))
    .ToDictionary(x => x[0], x => (string?)x[1], StringComparer.OrdinalIgnoreCase);

Console.WriteLine($"=== Demo {demo.Number}: {demo.Title} / {action.Label} ===");
var result = await DemoRunner.RunAsync(demo, action, values, options, diagnostics, admin);
Print(result);

static void Print(RunResult r)
{
    if (r.Error is not null)
    {
        Console.WriteLine($"ERROR: {r.Error}");
        if (r.ErrorHint is not null) Console.WriteLine($"HINT:  {r.ErrorHint}");
        return;
    }

    foreach (var q in r.Queries)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {q.Label}: {q.Rows:N0} rows in {q.ElapsedMs} ms");
        if (q.Sql is not null) Console.WriteLine(q.Sql);
        foreach (var p in q.Plans)
        {
            Console.WriteLine($"    plan: {(p.CompiledForThisRun ? "compiled for this run" : "reused")}, {(p.IsActual ? "last actual" : "estimated")}, " +
                              $"{p.LastLogicalReads:N0} reads, cost {p.Parsed.TotalCost:0.###}");
            foreach (var prm in p.Parsed.Parameters)
                Console.WriteLine($"    {prm.Name} {prm.DataType}: compiled {prm.CompiledValue ?? "-"}, runtime {prm.RuntimeValue ?? "-"}");
            PrintNode(p.Parsed.Root, 2);
        }
    }

    foreach (var t in r.Tables)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {t.Title}");
        Console.WriteLine("    " + string.Join(" | ", t.Columns));
        foreach (var row in t.Rows.Take(25)) Console.WriteLine("    " + string.Join(" | ", row));
    }

    foreach (var p in r.Panels)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {p.Title}");
        Console.WriteLine(p.Text);
    }

    foreach (var c in r.Charts)
    {
        Console.WriteLine();
        Console.WriteLine($"--- {c.Title}");
        foreach (var s in c.Series)
            Console.WriteLine($"    {s.Label}: " + string.Join(", ", c.Labels.Zip(s.Data, (l, v) => $"{l}={v:N0}")));
    }

    Console.WriteLine();
    foreach (var o in r.Observations) Console.WriteLine($"[{o.Kind}] {o.Text}");
    Console.WriteLine($"({r.TotalMs} ms total)");
}

static void PrintNode(PlanNode? n, int depth)
{
    if (n is null) return;
    var actual = n.ActualRows is { } a ? $", actual {a:N0}" : "";
    Console.WriteLine($"{new string(' ', depth * 2)}{n.PhysicalOp}{(n.ObjectName is null ? "" : " " + n.ObjectName)}  est {n.EstRowsTotal:N0}{actual}, {n.CostPercent:N0}%");
    foreach (var c in n.Children) PrintNode(c, depth + 1);
}
