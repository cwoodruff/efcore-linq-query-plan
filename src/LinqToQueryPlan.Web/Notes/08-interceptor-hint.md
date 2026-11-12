# Notes: interceptor-injected hints

EF Core has no API for query hints. What it has is `TagWith` and command interceptors, and together they do the job: tag the query, and an interceptor that sees the tag appends `OPTION (...)` before the command goes out.

## The code

The interceptor (`TagHintInterceptor.cs`) is about 30 lines. The query:

```csharp
db.Batting
    .Where(b => b.TeamId == key)
    .TagWith("hint:RECOMPILE")   // must be the first tag
    .TagWith("demo:hint");
```

The interceptor only reads the first comment line, which is why the hint tag goes first. That keeps it from ever touching a query by accident.

## What to look for

1. **The statement text in the plan cache.** It ends with `OPTION (RECOMPILE)` (or whichever hint you picked).
2. **What each hint does to the plans:**

| Hint | What happens | The cost |
| --- | --- | --- |
| `RECOMPILE` | A fresh plan every execution, built for that execution's value | Compile CPU on every call. Turns off PSP optimization for the statement. |
| `OPTIMIZE FOR UNKNOWN` | One plan built from the average density | Right for the typical value, wrong for both extremes |
| `USE HINT('DISABLE_PARAMETER_SNIFFING')` | Same as above | Also turns off PSP optimization |

3. **What disappears.** With `RECOMPILE`, the query vanishes from the plan cache: each execution compiles a throwaway plan that's never cached, so there's no plan to show and no reads in the plan cache DMVs. The Extended Events trace that loads under the result still has every execution. With `OPTIMIZE FOR UNKNOWN`, the plan is cached and the rare and common keys share it.

## When to reach for this

When one specific query has a sniffing problem, PSP declines to help (demo 1 shows how to find out), and you'd rather fix it in code than ask your DBA. `RECOMPILE` is the blunt instrument: fine for a query that runs a few times a minute, expensive for one that runs thousands of times a second.

Don't put a blanket interceptor on every query. I've seen it done. It turns every execution into a compile.

## Try this

- Run with `RECOMPILE`, then open **Server view, Plan cache**. Nothing new. Ask yourself what your monitoring tools read.
- Run with `OPTIMIZE FOR UNKNOWN` against Batting and against Hits, and compare the estimated rows to the average rows per key from demo 1's skew chart.

## Who owns the fix

You, in code, for exactly the queries you tag.

## Read more

- [Interceptors: command interception to add query hints](https://learn.microsoft.com/ef/core/logging-events-diagnostics/interceptors)
- [Query hints (Transact-SQL)](https://learn.microsoft.com/sql/t-sql/queries/hints-transact-sql-query)
