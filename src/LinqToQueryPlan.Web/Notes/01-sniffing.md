# Notes: the case of the slow team page

This is the case the whole talk follows. A team page is instant for a tiny 19th-century club and slow for the busiest team in the database. Same method, same LINQ, same SQL. The talk opens with the ticket, cracks it in Act 1, and closes it in Act 3 (demo 9).

## The code

```csharp
db.Hits
    .Where(h => h.TeamId == key)
    .Select(h => new SkewRow(h.YearId, h.PlayerId, h.HitNo))
    .TagWith("demo:sniff");
```

The demo looks up the rarest and most common team in the data at runtime (see `SkewedSource.cs`), so it never depends on values I hard-coded. Without the Hits table it falls back to `Batting.teamID`, which works but is much less dramatic. Build Hits on the Lab page first.

## Run it in this order

0. **Set up the case.** Runs `scripts/08-case-setup.sql`: turns Parameter Sensitive Plan optimization off and clears this database's plans and Query Store. At compat 170, PSP would give the busy team its own plan and there'd be no case. Plenty of production databases don't get that help, because they're below level 160 or PSP declined the query, and this makes the lab behave like one of them. Do it before the talk, not on stage.
1. **The ticket.** The small club runs first and compiles the plan: a seek plus a lookup per row, perfect for a handful of rows. The busy team runs next and reuses it, doing a lookup for every one of its hundreds of thousands of rows. Look at the reads.
2. **The false clue.** This is what most of us do next: call `ToQueryString()`, paste it into VS Code, run it, look at the plan. The demo does exactly that. The pasted batch uses a local variable (`DECLARE @key ... = N'CHN'`), which the optimizer doesn't sniff, so it builds a plan from the column's average. That's usually a different plan from the one your app is stuck with. On my test data: the app did 1.1 million reads with seek plus lookup, the window did 31 thousand with a scan. Tune the window's plan and you've tuned a plan your app never runs.

**Look at the skew** shows the distribution behind all of this. **Bonus** runs the busy team first, so the small club inherits a plan built for hundreds of thousands of rows.

## What to look for

- **What each run got**: the **Plan** column says **new** or **reused**. The **Compiled for** column is the value the plan was built for. When it doesn't match the key that ran, you're looking at sniffing. That one line of plan XML is the whole diagnosis, and it's how the case gets cracked in Act 1.
- On the reused plan, estimated rows stay at 1 while actual rows are in the hundreds of thousands.
- In the false clue, the **Same SQL text, two plans** table: plan shape, estimated rows, reads.

## Why the false clue happens

`ToQueryString()` turns each parameter into a `DECLARE` with the value assigned. That's fine for reading the SQL. Run in a query window, though, `@key` is a local variable, not a parameter. The optimizer can't see a local variable's value at compile time, so it estimates from the column's density: "an average team." Your app's plan was built for whichever team ran first after the last compile.

Two more things the pasted batch does:

- It gets its own plan cache entry and its own **query_id** in Query Store, with the same `query_hash` as your app's query. Demo 9 labels which is which. Put a hint on the pasted one and nothing changes for your users.
- If you edit the `DECLARE` into a literal, you get a plan compiled for that exact value, which is also not the plan your app has.

To see the plan your app actually ran, read it from the server: turn on `LAST_QUERY_PLAN_STATS` and use `sys.dm_exec_query_plan_stats`. That's what this lab does for every query.

## What SQL Server 2022 and 2025 do about it

Parameter Sensitive Plan optimization (compat 160 and up) keeps several plans for one parameterized statement: a dispatcher looks at the parameter value and picks a variant built for that range of row counts. It only engages on equality predicates with enough skew. On my test data `Batting.teamID` didn't qualify (the Extended Events reason was `SkewnessThresholdNotMet`) and `Hits.teamID` did. A covering index makes the whole problem disappear, because the seek is then right for every team.

Demo 9 turns PSP back on as the case's permanent fix.

## Who owns the fix

Spotting it is yours: compiled vs runtime value, from the real plan. The fix is a conversation. Demo 9 shows both sides: a Query Store hint on the query_id today, and the compatibility level (PSP) as the permanent fix.

## Read more

- [Parameter Sensitive Plan optimization](https://learn.microsoft.com/sql/relational-databases/performance/parameter-sensitive-plan-optimization)
- [sys.dm_exec_query_plan_stats](https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-views/sys-dm-exec-query-plan-stats-transact-sql)
