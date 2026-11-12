# Notes: Contains, three ways

`ids.Contains(x.Id)` is one of the most common lines of LINQ there is, and EF has translated it three different ways in three releases. Each way makes a different trade between plan cache size and what the optimizer knows about your list.

| EF version | SQL shape | What the optimizer knows | Plan cache |
| --- | --- | --- | --- |
| 7 and earlier | `IN ('DET', 'CHN', ...)` | Every value | One plan per distinct list |
| 8 and 9 | `IN (SELECT value FROM OPENJSON(@ids))` | Nothing about the list | One plan for everything |
| 10 (default) | `IN (@ids1, @ids2, ...)`, padded | How many values, roughly | One plan per padding bucket |

EF 10 lets you pick: `UseParameterizedCollectionMode(ParameterTranslationMode.Constant | Parameter | MultipleParameters)` globally, or `EF.Constant(list)` / `EF.Parameter(list)` per query.

## The code

```csharp
optionsBuilder.UseSqlServer(cs, o => o.UseParameterizedCollectionMode(mode));

db.Batting
    .TagWith($"demo:contains mode={mode}")
    .Where(b => b.YearId == year && teamIds.Contains(b.TeamId))
    .Select(b => new { b.TeamId, b.PlayerId, b.H });
```

The action runs two different lists each of 3, 8, 25, and 60 team IDs, under each mode. That's 24 queries.

## What to look for

1. **The three SQL panels** for the 8-value list. In `MultipleParameters` mode, count the parameters: there are 10. EF pads the list (repeating the last value) so lists of nearby sizes share one SQL text and one plan.
2. **The cached plans chart.** Reset first for clean numbers. Constant mode caches one plan per distinct list; Parameter mode caches one; MultipleParameters caches one per padding bucket, and both lists of the same size share it.
3. **The OPENJSON estimate.** In Parameter mode, find the table-valued function in the plan. The optimizer can't see inside a JSON string, so it guesses. On my test server it guessed 50 rows for a list of 8. A list of 5,000 gets the same guess.

## Try this

- Run it, then run it again with a different season. Only the Constant bar grows.
- In `Demo05ContainsThreeWays.cs`, change one list to `EF.Constant(teamIds)` and leave the mode alone. Per-query overrides beat the global setting.

## Gotchas

- Upgrading from EF 8 or 9 to 10 changes this translation for every `Contains` in your app. Microsoft calls it low impact, and for most queries it's an improvement. If a query relied on the OPENJSON shape and regresses, `ParameterTranslationMode.Parameter` puts it back.
- On EF Core 10.0.14, `ToQueryString()` threw a `NullReferenceException` for this query in `Parameter` mode on my machine. The query itself runs fine. When that happens the app shows the statement text from the plan cache instead.
- EF 11 adds `JSON_CONTAINS` for collections **stored** in JSON columns on SQL Server 2025. That's a different feature; it doesn't change how a list from your C# code is sent.

## Who owns the fix

You, in one line of configuration or one `EF.Constant`.

## Read more

- [What's new in EF Core 10: parameterized collections](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [Breaking changes in EF Core 10](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/breaking-changes)
