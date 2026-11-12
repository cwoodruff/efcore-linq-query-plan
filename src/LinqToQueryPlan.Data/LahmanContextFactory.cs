using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LinqToQueryPlan.Data;

public sealed record ContextSettings
{
    public required string ConnectionString { get; init; }

    // Section 1: this is EF's compatibility level, which decides the SQL EF writes.
    // The database's own compatibility level (scripts/01-setup-database.sql) decides what the optimizer does with it.
    public int CompatibilityLevel { get; init; } = 170;

    // Section 4: how a parameterized collection in Contains() is translated. Null = EF 10 default (MultipleParameters).
    public ParameterTranslationMode? CollectionMode { get; init; }

    // Section 6: with this off, EF 10 logs inlined constants as '?' and never logs parameter values.
    public bool SensitiveDataLogging { get; init; }

    // Section 5: global split/single default. Null = EF default (single, with a warning when multiple collections are included).
    public QuerySplittingBehavior? SplittingBehavior { get; init; }

    // Section 7, rung 2: inject OPTION(...) hints for queries tagged "hint:...".
    public bool EnableHintInterceptor { get; init; }

    // Receives the executed SQL (as EF logs it) and the multiple-collection-include warning.
    public Action<string>? Log { get; init; }
}

public static class LahmanContextFactory
{
    private static readonly EventId[] LoggedEvents =
    [
        RelationalEventId.CommandExecuted,
        RelationalEventId.MultipleCollectionIncludeWarning,
    ];

    public static LahmanContext Create(ContextSettings s)
    {
        var builder = new DbContextOptionsBuilder<LahmanContext>()
            .UseSqlServer(s.ConnectionString, sql =>
            {
                sql.UseCompatibilityLevel(s.CompatibilityLevel);
                if (s.CollectionMode is { } mode)
                {
                    sql.UseParameterizedCollectionMode(mode);
                }
                if (s.SplittingBehavior is { } split)
                {
                    sql.UseQuerySplittingBehavior(split);
                }
            })
            .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);

        if (s.SensitiveDataLogging)
        {
            builder.EnableSensitiveDataLogging();
        }

        if (s.Log is { } log)
        {
            builder.LogTo(log, LoggedEvents, LogLevel.Information, DbContextLoggerOptions.None);
        }

        if (s.EnableHintInterceptor)
        {
            builder.AddInterceptors(new TagHintInterceptor());
        }

        return new LahmanContext(builder.Options);
    }
}
