using System.Globalization;
using System.Xml.Linq;
using LinqToQueryPlan.Lab.Model;

namespace LinqToQueryPlan.Lab.Diagnostics;

// Turns showplan XML into a small tree the web app can draw and the console can print.
// Reads the same attributes the VS Code and SSMS plan viewers read: EstimateRows, EstimatedTotalSubtreeCost,
// RunTimeCountersPerThread/ActualRows, ParameterList, and the operator's Object and Predicate.
public static class ShowplanParser
{
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/sqlserver/2004/07/showplan";

    public static ParsedPlan Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        // A batch can hold several statements (a pasted ToQueryString has DECLARE, then SELECT). Take the one with the real plan.
        var stmt = doc.Descendants(Ns + "StmtSimple")
            .OrderByDescending(s => s.Descendants(Ns + "RelOp").Count())
            .FirstOrDefault();
        if (stmt is null) return new ParsedPlan();

        var qp = stmt.Element(Ns + "QueryPlan");
        var text = (string?)stmt.Attribute("StatementText") ?? "";
        var total = D(stmt, "StatementSubTreeCost");
        var rootRelOp = qp?.Element(Ns + "RelOp");

        var ppvIndex = text.IndexOf("PLAN PER VALUE", StringComparison.OrdinalIgnoreCase);

        var plan = new ParsedPlan
        {
            StatementText = text.Trim(),
            TotalCost = total,
            CardinalityModel = (string?)stmt.Attribute("CardinalityEstimationModelVersion"),
            OptimizationLevel = (string?)stmt.Attribute("StatementOptmLevel"),
            Root = rootRelOp is null ? null : ParseRelOp(rootRelOp, total > 0 ? total : D(rootRelOp, "EstimatedTotalSubtreeCost")),
            PlanPerValueHint = ppvIndex >= 0 ? Balance(text[ppvIndex..].Trim()) : null,
            HasDispatcher = qp?.Descendants(Ns + "Dispatcher").Any() == true,
        };

        foreach (var p in qp?.Element(Ns + "ParameterList")?.Elements(Ns + "ColumnReference") ?? [])
        {
            plan.Parameters.Add(new PlanParameter(
                (string?)p.Attribute("Column") ?? "?",
                (string?)p.Attribute("ParameterDataType"),
                (string?)p.Attribute("ParameterCompiledValue"),
                (string?)p.Attribute("ParameterRuntimeValue")));
        }

        foreach (var w in qp?.Element(Ns + "Warnings")?.Elements() ?? [])
        {
            plan.Warnings.Add(Describe(w));
        }

        return plan;
    }

    private static PlanNode ParseRelOp(XElement r, double statementCost)
    {
        var children = ChildRelOps(r).Select(c => ParseRelOp(c, statementCost)).ToList();
        var subtree = D(r, "EstimatedTotalSubtreeCost");
        var own = Math.Max(0, subtree - children.Sum(c => c.SubtreeCost));

        double? actualRows = null, actualExecs = null;
        var counters = r.Element(Ns + "RunTimeInformation")?.Elements(Ns + "RunTimeCountersPerThread").ToList();
        if (counters is { Count: > 0 })
        {
            actualRows = counters.Sum(c => D(c, "ActualRows"));
            actualExecs = counters.Sum(c => D(c, "ActualExecutions"));
        }

        var own_ = OwnDescendants(r).ToList();
        var obj = own_.FirstOrDefault(e => e.Name == Ns + "Object");
        string? objectName = null;
        if (obj is not null)
        {
            var parts = new[] { (string?)obj.Attribute("Table"), (string?)obj.Attribute("Index") }
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!.Trim('[', ']'));
            objectName = string.Join(".", parts);
            if (objectName.Length == 0) objectName = null;
        }

        var predicateHolder = own_.FirstOrDefault(e => e.Name == Ns + "SeekPredicates" || e.Name == Ns + "Predicate");
        var predicate = predicateHolder?.Descendants(Ns + "ScalarOperator").FirstOrDefault()?.Attribute("ScalarString")?.Value;
        if (predicate is { Length: > 200 }) predicate = predicate[..200] + "...";

        var node = new PlanNode
        {
            NodeId = (int)D(r, "NodeId"),
            PhysicalOp = (string?)r.Attribute("PhysicalOp") ?? "?",
            LogicalOp = (string?)r.Attribute("LogicalOp") ?? "",
            ObjectName = objectName,
            Predicate = predicate,
            EstRowsPerExecution = D(r, "EstimateRows"),
            EstExecutions = 1 + D(r, "EstimateRebinds") + D(r, "EstimateRewinds"),
            ActualRows = actualRows,
            ActualExecutions = actualExecs,
            SubtreeCost = subtree,
            OwnCost = own,
            CostPercent = statementCost > 0 ? 100.0 * own / statementCost : 0,
        };

        foreach (var w in r.Element(Ns + "Warnings")?.Elements() ?? [])
        {
            node.Warnings.Add(Describe(w));
        }

        node.Children.AddRange(children);
        return node;
    }

    // RelOps nested anywhere under this RelOp, but not under a deeper RelOp.
    private static IEnumerable<XElement> ChildRelOps(XElement relOp)
    {
        var stack = new Stack<XElement>(relOp.Elements().Reverse());
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            if (e.Name == Ns + "RelOp")
            {
                yield return e;
                continue;
            }
            foreach (var c in e.Elements().Reverse()) stack.Push(c);
        }
    }

    // Elements that belong to this operator (its Object, Predicate, Warnings), skipping child operators.
    private static IEnumerable<XElement> OwnDescendants(XElement relOp)
    {
        var stack = new Stack<XElement>(relOp.Elements().Reverse());
        while (stack.Count > 0)
        {
            var e = stack.Pop();
            if (e.Name == Ns + "RelOp") continue;
            yield return e;
            foreach (var c in e.Elements().Reverse()) stack.Push(c);
        }
    }

    // "PLAN PER VALUE(...))" carries the closing paren of the surrounding "option (". Drop unmatched trailing parens.
    private static string Balance(string s)
    {
        var open = s.Count(c => c == '(');
        while (s.Count(c => c == ')') > open && s.EndsWith(')')) s = s[..^1];
        return s;
    }

    private static string Describe(XElement w)
    {
        var attrs = string.Join(", ", w.Attributes().Select(a => $"{a.Name.LocalName}={a.Value}"));
        return attrs.Length == 0 ? w.Name.LocalName : $"{w.Name.LocalName} ({attrs})";
    }

    private static double D(XElement e, string attr) =>
        double.TryParse((string?)e.Attribute(attr), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;
}
