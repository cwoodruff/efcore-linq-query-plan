# Notes: optional parameters, EF vs OPPO

SQL Server 2025 added Optional Parameter Plan Optimization (OPPO) for the classic search-form pattern:

```sql
WHERE (teamID = @key OR @key IS NULL)
```

One cached plan can't be right for both cases: a seek when `@key` has a value, a scan when it's null. OPPO uses the same multi-plan machinery as PSP to keep one plan for each. It needs compatibility level 170.

The LINQ version of that pattern is:

```csharp
db.Batting.Where(b => key == null || b.TeamId == key)
```

So the question this demo answers is: does EF send the pattern OPPO was built for?

## What to look for

The two panels show the statement the server actually ran for `key = null` and for a real key.

On EF Core 10, they're different statements. For `null`, EF drops the filter entirely (`SELECT COUNT(*) FROM [Batting]`). For a value, it sends `WHERE [b].[teamID] = @key`. EF looked at the parameter on the client, saw it could simplify the predicate, and cached one SQL shape per case.

That means OPPO never sees the optional-parameter pattern from this LINQ. It also means you already have what OPPO would have built: two statements, two plans, each right for its case. The cost is visibility: your DBA sees two queries where you wrote one.

## Where OPPO still matters

- Stored procedures and hand-written SQL with `@p IS NULL OR ...`. That's most legacy search screens.
- `FromSql` queries that include the pattern.

The **Extended Events** view lists `query_with_optional_parameter_predicate` and `optional_parameter_optimization_skipped_reason` events if OPPO considered anything.

## Try this

- Add a second optional filter (`year == null || b.YearId == year`) in `SkewedSource.cs`, run every combination of null and non-null, and count the statements in **Server view, Plan cache**.
- Write the same pattern with `FromSql` and run it with and without a value. Then check the XE view for OPPO.

## Who owns the fix

Nobody, for EF. This demo exists because I wanted to know, and the answer changed a slide.

## Read more

- [Optional parameter plan optimization](https://learn.microsoft.com/sql/relational-databases/performance/optional-parameter-optimization)
- [Intelligent query processing in SQL databases](https://learn.microsoft.com/sql/relational-databases/performance/intelligent-query-processing)
