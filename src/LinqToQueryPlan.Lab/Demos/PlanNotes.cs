using System.Globalization;
using LinqToQueryPlan.Lab.Model;

namespace LinqToQueryPlan.Lab.Demos;

// Turns a captured plan into the sentences a DBA would say out loud while looking at it.
public static class PlanNotes
{
    public static void Describe(RunResult result, QueryRun run)
    {
        if (run.Plans.Count == 0)
        {
            result.Warn($"{run.Label}: nothing in the plan cache for this statement. With OPTION (RECOMPILE) (in the query or from a Query Store hint) each execution compiles a throwaway plan that is never cached, so the plan cache DMVs can't see it. Query Store and the Extended Events trace still can.");
            return;
        }

        foreach (var p in run.Plans)
        {
            var who = run.Plans.Count > 1 ? $"{run.Label} (statement {run.Plans.IndexOf(p) + 1})" : run.Label;

            if (p.CompiledForThisRun)
                result.Info($"{who}: compiled a new plan for this execution.");
            else
                result.Warn($"{who}: reused a plan compiled at {p.CreationTime:HH:mm:ss.fff} ({p.ExecutionCount} executions so far). The optimizer did not look at this run's values.");

            foreach (var prm in p.Parsed.Parameters.Where(x => x.CompiledValue is not null))
            {
                var used = prm.RuntimeValue ?? run.SentValues.GetValueOrDefault(prm.Name);
                if (used is not null && SqlText.Normalize(used) != SqlText.Normalize(prm.CompiledValue!))
                    result.Warn($"{who}: the plan was compiled for {prm.Name} = {SqlText.Normalize(prm.CompiledValue!)}, but this run sent {SqlText.Normalize(used)}.");
            }

            if (p.Parsed.PlanPerValueHint is not null)
                result.Good($"{who}: Parameter Sensitive Plan optimization picked a query variant: {p.Parsed.PlanPerValueHint}");

            // Ignore tiny row counts: 1 estimated vs 14 actual is noise, 1 vs 4,000 is the story.
            var worst = p.Parsed.AllNodes()
                .Where(n => n.MisestimateFactor is >= 10 && Math.Max(n.EstRowsTotal, n.ActualRows ?? 0) >= 100)
                .OrderByDescending(n => n.MisestimateFactor).FirstOrDefault();
            if (worst is not null)
                result.Warn($"{who}: {worst.PhysicalOp}{(worst.ObjectName is null ? "" : " on " + worst.ObjectName)} estimated {Fmt(worst.EstRowsTotal)} rows and got {Fmt(worst.ActualRows!.Value)} ({worst.MisestimateFactor:N0}x off).");

            var top = p.Parsed.AllNodes().OrderByDescending(n => n.CostPercent).FirstOrDefault();
            if (top is not null && top.CostPercent >= 40 && p.Parsed.NodeCount >= 4 && p.Parsed.TotalCost >= 0.1)
                result.Info($"{who}: {top.CostPercent:N0}% of the estimated cost is one {top.PhysicalOp}{(top.ObjectName is null ? "" : " on " + top.ObjectName)}.");

            foreach (var w in p.Parsed.Warnings)
                result.Warn($"{who}: plan warning {w}");

            if (!p.IsActual)
                result.Info($"{who}: showing the cached (estimated) plan. The engine keeps only a root node for simple queries' last actual plan, so actual rows come from sys.dm_exec_query_stats: {p.LastRows:N0} rows, {p.LastLogicalReads:N0} logical reads.");
        }
    }

    public static string Fmt(double v) => v >= 100 ? v.ToString("N0", CultureInfo.InvariantCulture) : v.ToString("0.#", CultureInfo.InvariantCulture);
}
