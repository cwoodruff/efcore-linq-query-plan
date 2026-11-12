namespace LinqToQueryPlan.Lab.Model;

// Everything one demo action produced. The web app renders it as HTML; the console prints it as text.
public sealed class RunResult
{
    public required string DemoSlug { get; init; }
    public required string ActionLabel { get; init; }
    public DateTime StartedUtc { get; } = DateTime.UtcNow;
    public long TotalMs { get; set; }

    public List<QueryRun> Queries { get; } = [];
    public List<Observation> Observations { get; } = [];
    public List<ChartSpec> Charts { get; } = [];
    public List<TableSpec> Tables { get; } = [];
    public List<TextPanel> Panels { get; } = [];
    public List<string> EfLog { get; } = [];

    // When set, the web app loads the XE view for this tag a few seconds after the result renders (XE dispatch latency).
    public string? DeferredXeTag { get; set; }

    public string? Error { get; set; }
    public string? ErrorHint { get; set; }

    public void Info(string text) => Observations.Add(new(ObservationKind.Info, text));
    public void Good(string text) => Observations.Add(new(ObservationKind.Good, text));
    public void Warn(string text) => Observations.Add(new(ObservationKind.Warn, text));
}

public enum ObservationKind { Info, Good, Warn }

public sealed record Observation(ObservationKind Kind, string Text);

public sealed class QueryRun
{
    public required string Label { get; init; }
    public required string Tag { get; init; }
    public string? Sql { get; set; }
    public long ElapsedMs { get; set; }
    public int Rows { get; set; }
    public List<CapturedPlan> Plans { get; } = [];

    // Parameter values as this run sent them, read from the DECLARE lines ToQueryString() prints.
    // The cached plan keeps only the values it was compiled for; these are what this execution actually used.
    public Dictionary<string, string> SentValues { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class TableSpec
{
    public required string Title { get; init; }
    public required string[] Columns { get; init; }
    public List<string[]> Rows { get; } = [];
    public string? Caption { get; init; }
}

// Panels that share a Group render side by side.
public sealed class TextPanel
{
    public required string Title { get; init; }
    public required string Text { get; init; }
    public string Language { get; init; } = "sql";
    public string? Group { get; init; }
    public string? Badge { get; init; }
}

public sealed class ChartSpec
{
    public string Id { get; } = "chart-" + Guid.NewGuid().ToString("n")[..10];
    public required string Title { get; init; }
    public string Type { get; init; } = "bar";
    public required IReadOnlyList<string> Labels { get; init; }
    public required IReadOnlyList<ChartSeries> Series { get; init; }
    public bool LogScale { get; init; }
    public string? YTitle { get; init; }
    public bool Horizontal { get; init; }
}

public sealed record ChartSeries(string Label, IReadOnlyList<double> Data);
