# Notes: what your log hides and your DBA sees

Sometimes you want the optimizer to see the actual values: a short, stable list where a plan per list is fine. `EF.Constant(list)` inlines the values into the SQL text instead of parameterizing them. EF 10 added a twist: those inlined values are now hidden in your logs.

## The code

```csharp
db.People
    .TagWith("demo:redaction")
    .Where(p => EF.Constant(countries).Contains(p.BirthCountry!))
    .CountAsync();
```

Run once with default logging and once with `EnableSensitiveDataLogging()`.

## What to look for

Three panels, side by side:

1. **Your log, default settings.** The country names are replaced with `?`. EF never logged parameter values by default; what's new in EF 10 is that it also redacts values it inlined into the SQL text.
2. **Your log, EnableSensitiveDataLogging.** The values are back.
3. **What the server ran.** The plan cache has the real values in the statement text, either way.

A few seconds later, the Extended Events trace loads under the result. It shows the same thing from the server's side: the real values, every time.

## Why this matters for debugging

Picture the conversation. Your log says `IN (?, ?, ?)`. Your DBA's trace says `IN (N'Cuba', N'Venezuela', N'Dominican Republic')`. Same query, two different texts, and you each think the other is looking at the wrong thing. Know which one you're looking at.

Leave sensitive data logging off in production. Turn it on locally when you need to line your log up with the server.

## How the trace finds these queries

The app sets `Application Name=LinqToQueryPlan` on the connection, and the XE session in `scripts/03-xe-session.sql` filters on it. If you don't set an application name yourself, EF 10 now adds one containing EF and SqlClient version information. Set your own: it's what your DBA filters on, and it avoids a subtle side effect where EF and non-EF code against the same database stop sharing a connection pool.

## Try this

- Change the country list. Each distinct list is a new SQL text and a new plan; that's the price of `EF.Constant`.
- Open the VS Code MSSQL extension's **Query Profiler** with the Standard template, filter ApplicationName to `LinqToQueryPlan`, and watch the queries arrive live.

## Who owns the fix

Nothing's broken. This one is about knowing which side of the wall you're reading.

## Read more

- [What's new in EF Core 10: redact inlined constants](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [Query Profiler in the MSSQL extension](https://learn.microsoft.com/sql/tools/visual-studio-code-extensions/mssql/mssql-query-profiler)
- [Extended Events overview](https://learn.microsoft.com/sql/relational-databases/extended-events/extended-events)
