namespace LinqToQueryPlan.Lab.Model;

// One cached plan pulled back from the server after a demo query ran.
// This is the plan EF actually got: same SQL text, same parameters, same plan cache entry.
public sealed class CapturedPlan
{
    public string Id { get; } = Guid.NewGuid().ToString("n");
    public required string PlanXml { get; init; }
    public required ParsedPlan Parsed { get; init; }

    // True when the XML came from sys.dm_exec_query_plan_stats (last actual plan, with per-operator actual rows).
    // False when only the cached (estimated) plan was available.
    public bool IsActual { get; init; }

    public DateTime CreationTime { get; init; }
    public DateTime LastExecutionTime { get; init; }
    public long ExecutionCount { get; init; }
    public long LastRows { get; init; }
    public long LastLogicalReads { get; init; }
    public double LastElapsedMs { get; init; }
    public string QueryHash { get; init; } = "";
    public string PlanHash { get; init; } = "";

    // creation_time on or after the moment the run started: the optimizer compiled this plan for this execution.
    public bool CompiledForThisRun { get; init; }
}

public sealed class ParsedPlan
{
    public string StatementText { get; init; } = "";
    public double TotalCost { get; init; }
    public string? CardinalityModel { get; init; }
    public string? OptimizationLevel { get; init; }
    public PlanNode? Root { get; init; }
    public List<PlanParameter> Parameters { get; } = [];
    public List<string> Warnings { get; } = [];

    // Parameter Sensitive Plan optimization: the variant hint the engine appends to the statement text.
    public string? PlanPerValueHint { get; init; }
    public bool HasDispatcher { get; init; }

    public IEnumerable<PlanNode> AllNodes()
    {
        if (Root is null) yield break;
        var stack = new Stack<PlanNode>([Root]);
        while (stack.Count > 0)
        {
            var n = stack.Pop();
            yield return n;
            for (var i = n.Children.Count - 1; i >= 0; i--) stack.Push(n.Children[i]);
        }
    }

    public int NodeCount => AllNodes().Count();
}

public sealed class PlanNode
{
    public int NodeId { get; init; }
    public string PhysicalOp { get; init; } = "";
    public string LogicalOp { get; init; } = "";
    public string? ObjectName { get; init; }
    public string? Predicate { get; init; }
    public double EstRowsPerExecution { get; init; }
    public double EstExecutions { get; init; } = 1;
    public double EstRowsTotal => EstRowsPerExecution * EstExecutions;
    public double? ActualRows { get; init; }
    public double? ActualExecutions { get; init; }
    public double SubtreeCost { get; init; }
    public double OwnCost { get; init; }
    public double CostPercent { get; init; }
    public List<string> Warnings { get; } = [];
    public List<PlanNode> Children { get; } = [];

    // How far off the estimate was, as a factor >= 1. Null when there are no actuals.
    public double? MisestimateFactor
    {
        get
        {
            if (ActualRows is not { } actual) return null;
            var est = Math.Max(EstRowsTotal, 1);
            var act = Math.Max(actual, 1);
            return Math.Max(est / act, act / est);
        }
    }
}

public sealed record PlanParameter(string Name, string? DataType, string? CompiledValue, string? RuntimeValue);
