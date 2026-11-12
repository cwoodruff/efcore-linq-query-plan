# Getting started

This is the demo lab for my talk **From LINQ to Query Plan: Teaching Your Queries to Respect the Optimizer**. Every demo runs a real EF Core 10 query against SQL Server 2025, then pulls the plan that query actually got back out of the server's plan cache and shows it to you next to the C# that produced it.

That last part matters. The usual way to look at an EF query's plan is to copy the `ToQueryString()` output into a query window and run it. That output declares the values as local variables (`DECLARE @key ... = N'CHN'`), and SQL Server doesn't sniff local variables. You get a plan built from average density, which is not the plan EF got. This app skips the copy and paste and reads the real one.

## What you need

| Piece | Version | Notes |
| --- | --- | --- |
| .NET SDK | 10.0 | `dotnet --version` should print 10.x |
| SQL Server | 2025 (17.x) | In Docker is fine. Developer or Evaluation edition. |
| Lahman database | 2025 edition | Restored as `lahman2025` |
| An editor | Rider, VS Code, or Visual Studio | VS Code's MSSQL extension can open the `.sqlplan` files this app exports |

No SSMS needed. I build and present this on a Mac.

## 1. Start SQL Server 2025

```bash
docker run -d --name sql2025 \
  -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Your_Strong_Passw0rd" \
  -p 1433:1433 mcr.microsoft.com/mssql/server:2025-latest
```

On Apple Silicon, Docker Desktop runs the image under Rosetta. It's slower than native but fine for these demos.

## 2. Get the Lahman database

The demos expect the `lahman2025` schema: natural composite keys, plus the clustered and nonclustered indexes listed in `scripts/00-environment-check.sql`.

> The lahman2025 backup file (.bak) is included in this repo under the /db folder.

The original data comes from the Lahman Baseball Database (CC BY-SA 3.0), now maintained through SABR.

## 3. Point the app at it

Either edit `src/LinqToQueryPlan.Web/appsettings.Development.json`:

```json
{
  "Lab": {
    "ConnectionString": "Server=127.0.0.1,1433;Database=lahman2025;User Id=sa;Password=Your_Strong_Passw0rd;Encrypt=False;TrustServerCertificate=True",
    "AllowAdminActions": true
  }
}
```

or set an environment variable (the console app reads the same one):

```bash
export LAHMAN_CONNECTION='Server=127.0.0.1,1433;Database=lahman2025;User Id=sa;Password=Your_Strong_Passw0rd;Encrypt=False;TrustServerCertificate=True'
```

Three details in that string have each cost me time:

- **Single quotes.** zsh (the macOS default shell) expands `!` inside double quotes, so a password like `abc!!` quietly turns into `abc` plus your last command. Single quotes pass it through untouched.
- **`127.0.0.1`, not `localhost`.** On macOS `localhost` can resolve to the IPv6 address `::1` first, and Docker's port forward doesn't always answer there the same way.
- **`Encrypt=False`.** SqlClient encrypts by default. A local container has nothing to protect over a loopback connection, and skipping TLS removes the step most likely to stall. Keep encryption on for anything that isn't your own laptop.

If something still spins, open **Lab setup** and press **Run connection check**, or run `dotnet run --project src/LinqToQueryPlan.Demos -- doctor`. It tries name lookup, the TCP port, and the SQL login one at a time, each with a short limit, and says which step failed. The password is never printed, only its length, so you can paste the output into an issue.

You don't need to set `Application Name`. The app sets it for you: demo queries run as `LinqToQueryPlan` (the Extended Events session filters on that) and the app's own diagnostic queries run as `LinqToQueryPlan.Diagnostics` so they stay out of the trace.

## 4. Run it

```bash
dotnet run --project src/LinqToQueryPlan.Web
```

Then open http://localhost:5180 and go to **Lab setup**. Every red check has a fix, and the **Setup** button runs `scripts/01-setup-database.sql` and `scripts/03-xe-session.sql` for you. Setup does four things the demos need:

1. Compatibility level 170, so SQL Server 2025's optimizer features are on. A restored backup keeps its old level.
2. Query Store capture mode ALL. The default, AUTO, skips cheap and infrequent queries, which is every demo query.
3. `LAST_QUERY_PLAN_STATS = ON`, so the app can show actual rows per operator.
4. An Extended Events session called `LinqToQueryPlan`.

## 5. Build the Hits table

The case in demo 1 needs a big, skewed table. The **Build Hits** button on the Lab page runs `scripts/06-inflate-for-psp.sql`, which creates one row per hit (several million rows) with a nonclustered index on `teamID`. Without it, demo 1 falls back to `Batting.teamID`, which works but is much less dramatic.

## 6. The case file

The demos are numbered in the order the talk uses them, and one case runs through them:

| When | Demo | What happens to the case |
| --- | --- | --- |
| Before the talk | 1, step 0 | **Set up the case**: PSP off, plans and Query Store cleared (`scripts/08`) |
| Opening | 1, steps 1 and 2 | The ticket (the busy team is slow) and the false clue (ToQueryString shows a different plan) |
| Act 1: See it | 2, 3 | Reading a plan you didn't write, then tags and parameter names. The case is cracked by the compiled vs runtime value from step 1 |
| Act 2: Shape it | 4, 5, 6 | What's yours to change in LINQ: optional parameters, Contains, split vs single |
| Act 3: Hand it off | 7, 8, 9 | What the DBA sees, the price of RECOMPILE, and the case closed with a query_id (`scripts/09` turns PSP back on) |

Run demo 1's step 0 again any time you want to replay the case from the start.

## How the pages work

Each demo page has three parts:

- **Actions** at the top. Each one runs real EF Core code from `src/LinqToQueryPlan.Lab/Demos`. Read the code alongside the page; it's short.
- **Results**, newest first. Each result shows what EF emitted, what the optimizer saw (the cached plan as a tree, with estimated and actual rows), and a few sentences pointing at the interesting part.
- **Server view** at the bottom: the plan cache, Query Store, and the Extended Events trace for that demo's queries. That's the DBA's side of the wall.

The notes column on the right (you're reading the long version of it) can be hidden with the **Notes** button. **A+** makes everything bigger for a projector.

## Admin actions and safety

Setup, Reset, Build Hits, the case setup and PSP buttons, and the Query Store hint buttons change server state. `Reset` runs `DBCC FREEPROCCACHE`. They only run when `Lab:AllowAdminActions` is true (it is in Development) **and** the server is local (`127.0.0.1`, `localhost`, or a host you add to `Lab:AdminHosts`). Don't point this at a shared server.

## The console version

Same demos, same code, printed as text, in case the browser misbehaves on stage:

```bash
dotnet run --project src/LinqToQueryPlan.Demos            # list demos and actions
dotnet run --project src/LinqToQueryPlan.Demos -- 3 rare-then-common
```

## Read more

- [What's new in EF Core 10](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew)
- [What's new in SQL Server 2025](https://learn.microsoft.com/sql/sql-server/what-s-new-in-sql-server-2025)
- [Query processing architecture guide](https://learn.microsoft.com/sql/relational-databases/query-processing-architecture-guide) (parameter sniffing and local variables)
- [MSSQL extension for VS Code](https://learn.microsoft.com/sql/tools/visual-studio-code-extensions/mssql/mssql-extension-visual-studio-code)
