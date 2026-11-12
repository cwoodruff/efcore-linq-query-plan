# Notes: the cold open

I start the talk here because it's the whole talk in one query. Five lines of LINQ, five tables, and a plan none of us wrote.

## What the code does

Pitchers on the World Series winner for a season, with the franchise name. Five joins on composite keys (`yearID`, `lgID`, `teamID`), a filter on `SeriesPost`, and a `Distinct()`.

```csharp
(from sp in db.SeriesPost
 join t in db.Teams on new { sp.YearId, L = sp.LgIdWinner, T = sp.TeamIdWinner }
                    equals new { t.YearId, L = t.LgId, T = t.TeamId }
 join f in db.TeamFranchises on t.FranchId equals f.FranchId
 join p in db.Pitching on new { t.YearId, t.LgId, t.TeamId }
                       equals new { p.YearId, p.LgId, p.TeamId }
 join pp in db.People on p.PlayerId equals pp.PlayerId
 where sp.Round == round && sp.YearId == year
 select new { pp.NameFirst, pp.NameLast, Franchise = f.FranchName })
.Distinct()
.TagWith("demo:coldopen");
```

## What to look for

1. **What EF emitted.** Expand the query in the result. Notice that `round` became a parameter (`@round`) even though it's always `"WS"`. EF parameterizes anything that comes from a variable, and inlines literals. That one choice shapes every demo that follows.
2. **What the optimizer saw.** The plan tree reads from the leaves up: data starts at the index operators at the bottom and flows toward the root. Each row shows the operator's share of the estimated cost and two bars: estimated rows (blue) and actual rows (green). Bars are log scale so one row and a million rows fit on the same screen.
3. **Where estimates and actuals disagree.** Operators that are off by 10x or more on a meaningful number of rows get a terracotta edge and a multiplier. Small misses on tiny row counts don't count.

## Reading a plan tree, the short version

| You see | It means |
| --- | --- |
| Index Seek | Went straight to the rows it needed |
| Index Scan / Clustered Index Scan | Read the whole index |
| Key Lookup / Clustered Index Seek under a Nested Loops | Went back to the table for columns the index didn't have, once per row |
| Estimated way below actual | The optimizer planned for a small result and got a big one |

One thing that surprises people in this database: on `Batting`, `Pitching`, `Fielding`, and `Appearances` the clustered index is not the primary key. It's clustered on `playerID, yearID, stint` and not unique, so SQL Server adds a hidden uniquifier. The primary key is a separate nonclustered index.

## Try this

- Change the season. Years with a big postseason roster produce more rows; the plan shape may not change, but the actuals will.
- Download the `.sqlplan` and open it in VS Code with the MSSQL extension. It's the same plan, with every property the app doesn't show.
- Open **Server view, Plan cache**. One entry for this tag no matter how many seasons you try. That's parameterization doing its job.

## Who owns the fix

Nobody yet. This demo is about learning to see. The fixes start in demo 1.

## Read more

- [Display and save execution plans](https://learn.microsoft.com/sql/relational-databases/performance/display-and-save-execution-plans)
- [sys.dm_exec_query_plan_stats](https://learn.microsoft.com/sql/relational-databases/system-dynamic-management-objects/sys-dm-exec-query-plan-stats-transact-sql) (where the actual rows come from)
