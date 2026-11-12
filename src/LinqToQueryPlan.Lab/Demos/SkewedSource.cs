using LinqToQueryPlan.Data;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Lab.Demos;

// Shared by demos 1, 4, 8, and 9: one skewed equality predicate, resolved at runtime.
//
//   Hits.teamID       if scripts/06-inflate-for-psp.sql has been run (millions of rows, skew from 1 row to hundreds of thousands)
//   Batting.teamID    otherwise, or when source=batting (about 128k rows, roughly 10x skew)
//
// The rare and common key values are read from the data, not hard-coded, so the demo cannot drift from the database.
public sealed class SkewedSource
{
    public required string Table { get; init; }
    public required string Column { get; init; }
    public required string RareKey { get; init; }
    public required int RareCount { get; init; }
    public required string CommonKey { get; init; }
    public required int CommonCount { get; init; }
    public required int DistinctKeys { get; init; }
    public required double AvgPerKey { get; init; }
    public required IReadOnlyList<(string Key, int Count)> Head { get; init; }
    public required IReadOnlyList<(string Key, int Count)> Tail { get; init; }
    public string? Notice { get; init; }
    public required Func<LahmanContext, string, IQueryable<SkewRow>> Query { get; init; }
    public required Func<LahmanContext, string?, IQueryable<SkewRow>> OptionalQuery { get; init; }

    public string Describe() =>
        $"{Table}.{Column}: rare key '{RareKey}' has {RareCount:N0} rows, common key '{CommonKey}' has {CommonCount:N0} rows " +
        $"({DistinctKeys:N0} distinct keys, {AvgPerKey:N0} rows per key on average).";

    public static async Task<SkewedSource> ResolveAsync(LahmanContext db, string source, LabSession? session = null)
    {
        if (session is not null) session.Step = "finding the rarest and most common key (GROUP BY on the demo table)";
        var wantBatting = source.Equals("batting", StringComparison.OrdinalIgnoreCase);
        var hitsExists = !wantBatting && await HitsTableExistsAsync(db);

        if (hitsExists)
        {
            var counts = await db.Hits.GroupBy(h => h.TeamId)
                .Select(g => new { g.Key, N = g.Count() })
                .ToListAsync();
            return Build("Hits", "teamID", counts.Select(x => (x.Key, x.N)).ToList(), null,
                (ctx, key) => ctx.Hits.Where(h => h.TeamId == key).Select(h => new SkewRow(h.YearId, h.PlayerId, h.HitNo)),
                (ctx, key) => ctx.Hits.Where(h => key == null || h.TeamId == key).Select(h => new SkewRow(h.YearId, h.PlayerId, h.HitNo)));
        }

        var byTeam = await db.Batting.GroupBy(b => b.TeamId)
            .Select(g => new { g.Key, N = g.Count() })
            .ToListAsync();
        var notice = wantBatting ? null : "Hits table not found, so this uses Batting.teamID. Build Hits from the Lab page (script 06) for a bigger, more skewed table.";
        return Build("Batting", "teamID", byTeam.Select(x => (x.Key, x.N)).ToList(), notice,
            (ctx, key) => ctx.Batting.Where(b => b.TeamId == key).Select(b => new SkewRow(b.YearId, b.PlayerId, b.H ?? 0)),
            (ctx, key) => ctx.Batting.Where(b => key == null || b.TeamId == key).Select(b => new SkewRow(b.YearId, b.PlayerId, b.H ?? 0)));
    }

    private static SkewedSource Build(string table, string column, List<(string Key, int Count)> counts, string? notice,
        Func<LahmanContext, string, IQueryable<SkewRow>> query, Func<LahmanContext, string?, IQueryable<SkewRow>> optional)
    {
        var ordered = counts.OrderBy(x => x.Count).ThenBy(x => x.Key).ToList();
        return new SkewedSource
        {
            Table = table,
            Column = column,
            RareKey = ordered[0].Key,
            RareCount = ordered[0].Count,
            CommonKey = ordered[^1].Key,
            CommonCount = ordered[^1].Count,
            DistinctKeys = ordered.Count,
            AvgPerKey = ordered.Average(x => x.Count),
            Head = ordered.TakeLast(15).Reverse().ToList(),
            Tail = ordered.Take(15).ToList(),
            Notice = notice,
            Query = query,
            OptionalQuery = optional,
        };
    }

    private static async Task<bool> HitsTableExistsAsync(LahmanContext db)
    {
        var count = await db.Database
            .SqlQueryRaw<int>("SELECT COUNT(*) AS [Value] FROM sys.tables WHERE name = N'Hits'")
            .SingleAsync();
        return count > 0;
    }
}

public sealed record SkewRow(short YearId, string Label, int N);
