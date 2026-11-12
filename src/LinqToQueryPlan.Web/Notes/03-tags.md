# Notes: tags, call sites, and parameter names

Before you can talk to a DBA about a query, you both have to be able to find it. In a busy plan cache, an EF query is an anonymous `SELECT` with a parameter list. This demo gives it a name.

## The code

```csharp
db.Batting
    .TagWith("demo:tags")
    .TagWith("Roster batting lines for one team-season")
    .TagWithCallSite()
    .Where(b => b.TeamId == teamId && b.YearId == yearId)
    .OrderByDescending(b => b.Hr)
    .Select(b => new { b.Player.NameFirst, b.Player.NameLast, b.Hr, b.Rbi });
```

## What to look for

1. **The comment lines at the top of the SQL.** `TagWith` adds a comment. `TagWithCallSite()` adds the source file and line number for free. Both travel to the server with the batch text, so the plan cache and Extended Events show them.
2. **Parameter names.** EF 10 names parameters after your variables: `@teamId` and `@yearId`. Before EF 10 they were `@__teamId_0`. Plan XML and Query Store are much easier to read now.
3. **Parameter types.** The "Parameters as the server saw them" table shows `nvarchar(4)` and `smallint`, matching the columns. That's because the model declares `HasMaxLength(4)`. Without it, EF sends `nvarchar(4000)`; a type that doesn't match the column can show up as an implicit conversion in the plan.
4. **The query_id.** Open **Server view, Query Store**. That number is what you hand a DBA, and what they hand back with a hint attached (demo 9).

## A warning about upgrading

Because EF 10 changed the parameter names, it changed the SQL text of nearly every query your app sends. Different text means a different plan cache entry. Microsoft's breaking-changes note says to expect a compilation spike right after you deploy an EF 10 upgrade, while every plan is rebuilt. Tell your DBA before it happens, not after.

## Where tags don't survive

Tags are comments in front of the statement, and two places only keep the statement:

- **Query Store.** `query_sql_text` starts at `SELECT`. Search it for your tag and you'll find nothing. The batch the query was compiled from still has the tag, through `last_compile_batch_sql_handle`; that's how this app (and `scripts/04-query-store.sql`) finds your query_id.
- **Parameter Sensitive Plan variants** (demo 1). Each variant is cached as its own statement starting at `SELECT`. Query Store links variants to their tagged parent in `sys.query_store_query_variant`.

If your DBA says "I searched Query Store for your tag and it isn't there," they're right. Send them the query from section A of `scripts/04-query-store.sql`.

## Try this

- Change the team and season, then look at the plan cache: still one entry for the tag.
- Remove `HasMaxLength(4)` from `TeamId` in `LahmanContext.cs`, run again, and look at the parameter type and the plan.
- Parameter names come from the C# closure, so nesting shows. Capture a variable through a lambda inside another lambda and EF 10 names it something like `@_8__locals2_8__locals1_year`. Copying it into a local in the inner scope gives you `@year` back. Demo 5's code has the fix and a comment.

## Who owns the fix

You. Tagging is free, and it's the first thing a DBA will ask for.

## Read more

- [Query tags](https://learn.microsoft.com/ef/core/querying/tags)
- [Breaking changes in EF Core 10](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/breaking-changes) (simplified parameter names)
