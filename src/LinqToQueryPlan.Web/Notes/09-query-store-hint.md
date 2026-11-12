# Notes: case closed, with your query_id

The end of the case from demo 1. The developer brings a query_id, the compiled vs runtime value, a tag, and an app name. The DBA has two fixes, and neither touches your code: a Query Store hint today, and turning Parameter Sensitive Plan optimization back on as the permanent fix.

## The steps

1. **Run the case.** Unchanged code: the small club, then the busy team. With the case set up (demo 1, step 0), the busy team reuses the small club's plan.
2. **Find the query_id.** Query Store's statement text doesn't include your tag (it starts at `SELECT`), so the lab finds the query through the batch it was compiled from. Put the hint on the row marked **your app (sp_executesql)**. If you ran the false clue in demo 1, there's also an **ad hoc batch (a query window)** row with the same query_hash and a different query_id. A hint there does nothing for your users.
3. **Apply a hint, as the DBA.** Paste the query_id, pick a hint. Under the covers:

```sql
EXEC sys.sp_query_store_set_hints
     @query_id = 42,
     @query_hints = N'OPTION (RECOMPILE)';
```

4. **Run step 1 again.** Same binary, same SQL text. Each team now gets a plan compiled for its own value.
5. **Clear the hint.** (`sys.sp_query_store_clear_hints`.) Always, before the next session.
6. **The permanent fix: turn PSP back on.** Runs `scripts/09-case-psp-on.sql` and the case again. Each team gets its own PSP variant, with no hint and no RECOMPILE. In production, the same fix is usually raising the database compatibility level to 160 or 170, which is the DBA's call.

## What to look for

- After step 4 with `RECOMPILE`, the plan view goes empty. A recompiled statement never enters the plan cache, so there's nothing for the app (or your monitoring) to pull back. Open the **Extended Events** panel: the reads for the busy team dropped from over a million to tens of thousands. Query Store's execution counts still show every run.
- After step 6, the **PSP variant** badges, and the Query Store variants table.

## Things your DBA already knows

- Query Store hints win over hints in the query text and over plan guides.
- They outlive the data that justified them. Write down why a hint is there.
- They're attached to the query_id, not the text. Clearing Query Store (demo 1, step 0) gives the query a new id.

## Who owns the fix

Your DBA, with your query_id. Bring a query_id, not a complaint.

## Read more

- [Query Store hints](https://learn.microsoft.com/sql/relational-databases/performance/query-store-hints)
- [Query Store hints best practices](https://learn.microsoft.com/sql/relational-databases/performance/query-store-hints-best-practices)
- [Parameter Sensitive Plan optimization](https://learn.microsoft.com/sql/relational-databases/performance/parameter-sensitive-plan-optimization)
