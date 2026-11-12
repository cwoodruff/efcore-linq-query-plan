# Notes: split vs single query

`Include` two collections that sit side by side on the same entity and EF writes one SQL statement with two joins. Every batting row for a team gets paired with every pitching row for that team. That's the cartesian explosion, and the database does all that work before EF throws most of it away.

## The code

```csharp
db.Teams
    .Where(t => t.YearId == year)
    .Include(t => t.Batting)
    .Include(t => t.Pitching)
    .AsSingleQuery();   // or .AsSplitQuery()
```

## What to look for

1. **The rows chart.** Single query: roughly the sum, over every team, of batters times pitchers. Split query: teams plus batting rows plus pitching rows. Same objects in memory either way.
2. **The plans.** Single query is one plan, wide, with a big sort to put rows back together. Split query is three small plans (teams, then batting joined to those teams, then pitching joined to those teams), each with its own roundtrip.
3. **The EF log.** EF warns about the single query when you load more than one collection without choosing a splitting behavior. The warning is in the log at the bottom of the result.

## Split isn't automatically faster

It's a different deal:

- More roundtrips. On a laptop that's nothing; across a slow network it adds up.
- No single snapshot. Each statement sees the data as of when it runs. If rows change between statements, the pieces can disagree. EF 11 now throws `DbQueryConcurrencyException` when it detects that, instead of quietly handing you empty collections.
- EF 10 fixed a split query ordering bug with `Skip`/`Take` where the statements could disagree about which parent rows they were loading.

## Coming in EF 11

EF 11 removes unneeded joins to reference navigations from split query statements and drops redundant `ORDER BY` columns from both shapes. Same LINQ, smaller plans. Microsoft measured 29% faster on a split query scenario and 22% on a single query scenario. Your numbers will vary.

## Try this

- Pick an early season (1880s) with fewer, smaller rosters, and compare.
- Configure split as the default with `UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)` and use `AsSingleQuery()` where you need one snapshot.

## Who owns the fix

You. It's one operator on the query, and the right answer depends on how many collections you load and how far away your database is.

## Read more

- [Single vs. split queries](https://learn.microsoft.com/ef/core/querying/single-split-queries)
- [What's new in EF Core 11](https://learn.microsoft.com/ef/core/what-is-new/ef-core-11.0/whatsnew)
