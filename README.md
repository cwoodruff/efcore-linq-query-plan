# From LINQ to Query Plan: demo lab

Companion code for the talk **From LINQ to Query Plan: Teaching Your Queries to Respect the Optimizer**.

A Razor Pages + htmx web app that runs nine EF Core 10 demos against SQL Server 2025 and the Lahman baseball database, then pulls each query's real cached plan back from the server and draws it next to the C# that produced it. Notes for each demo live in `src/LinqToQueryPlan.Web/Notes` and render in the app; they read fine on GitHub too.

Start with [Getting started](src/LinqToQueryPlan.Web/Notes/getting-started.md). For the whole talk, slide by slide with every demo explained, read the [learning guide](docs/learning-guide.md).

## Run it

```bash
export LAHMAN_CONNECTION='Server=127.0.0.1,1433;Database=lahman2025;User Id=sa;Password=...;Encrypt=False;TrustServerCertificate=True'
dotnet run --project src/LinqToQueryPlan.Demos -- doctor   # optional: checks the connection step by step
dotnet run --project src/LinqToQueryPlan.Web
```

Open http://localhost:5180, go to **Lab setup**, press **Setup**.

Use single quotes around the connection string: zsh expands `!` inside double quotes, which mangles passwords that contain it. If anything spins, the **Run connection check** button on Lab setup says which step is stuck. Every database call now has a time limit, so a stall shows up as an error card with a hint instead of a spinner.

## Layout

```
scripts/                         T-SQL, numbered in run order. The web app runs 01, 03, 05, 06 from these files.
  00-environment-check.sql       read-only: engine, compat level, schema, skew
  01-setup-database.sql          compat 170, Query Store capture ALL, LAST_QUERY_PLAN_STATS, optimizer features
  02-plan-cache-by-tag.sql       cached plans grouped by TagWith, compiled vs runtime parameter values
  03-xe-session.sql              Extended Events session filtered on Application Name, with PSP and OPPO events
  04-query-store.sql             query_id by tag (through the batch handle), PSP variants, Query Store hints
  05-reset-between-demos.sql     laptop only: clear plan cache and Query Store, restart XE
  06-inflate-for-psp.sql         dbo.Hits, one row per hit, indexed on teamID: the table behind the case
  07-read-xe-ring-buffer.sql     reads the XE session with T-SQL
  08-case-setup.sql              laptop only: sets up the case (PSP off, plans and Query Store cleared)
  09-case-psp-on.sql             laptop only: the case's permanent fix (PSP back on)
src/LinqToQueryPlan.Data/        hand-mapped LahmanContext, context factory, TagHintInterceptor
src/LinqToQueryPlan.Lab/         the demos and the diagnostics (plan capture, showplan parser, Query Store, XE)
src/LinqToQueryPlan.Web/         Razor Pages + htmx UI, Chart.js charts, learner notes
src/LinqToQueryPlan.Demos/       console fallback: same demos, printed as text
```

The demo code is in one place (`LinqToQueryPlan.Lab/Demos`). The web app and the console both render its results.

## Demos

| # | Page | Talk act | What it proves |
| --- | --- | --- | --- |
| 1 | `/demos/sniffing` | The case | The slow team page: sniffing, and the false clue (ToQueryString shows a different plan) |
| 2 | `/demos/cold-open` | 1. See it | Five joins, one plan you didn't write |
| 3 | `/demos/tags` | 1. See it | Tags, call sites, EF 10 parameter names, the query_id |
| 4 | `/demos/optional-parameter` | 2. Shape it | Whether EF ever sends the pattern OPPO optimizes |
| 5 | `/demos/contains` | 2. Shape it | Contains under Constant, Parameter, MultipleParameters |
| 6 | `/demos/split-vs-single` | 2. Shape it | Cartesian explosion vs three small plans |
| 7 | `/demos/redaction` | 3. Hand it off | EF 10 log redaction vs what the server received |
| 8 | `/demos/interceptor-hint` | 3. Hand it off | OPTION hints via a tag and a DbCommandInterceptor, and what RECOMPILE costs |
| 9 | `/demos/query-store-hint` | 3. Hand it off | Case closed: a Query Store hint on your query_id, then PSP as the permanent fix |

## How plans are captured

Each demo query carries `TagWith("demo:<name>")`. Before it runs, the app snapshots `sys.dm_exec_query_stats` for statements with that tag; after it runs, it takes the statements whose execution count went up. For each one it reads `sys.dm_exec_query_plan_stats` (last actual plan, needs `LAST_QUERY_PLAN_STATS = ON`) and falls back to the cached estimated plan when the engine only kept a root node. Parameter Sensitive Plan variants don't carry the tag, so they're matched through the tagged dispatcher's query hash.

This is why the app doesn't ask you to paste `ToQueryString()` output into a query window: that output declares values as local variables, the optimizer doesn't sniff those, and you'd be looking at a different plan.

## Tooling

Built and presented on macOS with Rider and VS Code. No SSMS needed. Downloaded `.sqlplan` files open in VS Code's MSSQL extension. Front-end libraries (htmx 2.0.4, Chart.js 4.4.1, highlight.js 11.9.0) are vendored in `wwwroot/lib`, so the app runs with no network, which matters on conference wifi.

## Safety

Admin actions (Setup, Reset, Build Hits, Query Store hints) run only when `Lab:AllowAdminActions` is true (Development) and the server is local. Reset runs `DBCC FREEPROCCACHE`. Don't point this at a shared server.
