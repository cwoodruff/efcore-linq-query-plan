-- 04-query-store.sql
-- Section 3 (PSP variants), section 6 (Query Store as the conversation with your DBA),
-- section 7 rung 3 (Query Store hints, no redeploy).

USE lahman2025;
GO

-- A. Find a demo query by its tag and get the query_id. The query_id is the unit of conversation with a DBA.
--    Query Store's query_sql_text starts at the statement (SELECT), so the TagWith comment is NOT in it.
--    The batch the query was compiled from still has the tag: reach it through last_compile_batch_sql_handle.
EXEC sys.sp_query_store_flush_db;   -- push in-memory stats to the views so a just-run query shows up
DECLARE @tag nvarchar(100) = N'demo:sniff';
SELECT q.query_id, CONVERT(varchar(20), q.query_hash, 1) AS query_hash, qt.query_sql_text
FROM sys.query_store_query AS q
JOIN sys.query_store_query_text AS qt ON qt.query_text_id = q.query_text_id
CROSS APPLY sys.dm_exec_sql_text(q.last_compile_batch_sql_handle) AS bt
WHERE bt.text LIKE N'%-- ' + @tag + N'%'
  AND bt.text NOT LIKE N'%query_store%';
GO

-- B. PSP in Query Store: parent query, dispatcher plan, and the variant plans the engine built for it.
--    Empty result means PSP did not engage. Check the XE session for the skip reason.
--    Variants have no tag at all (their text starts at SELECT); this view is how you get from the tagged parent to them.
DECLARE @tag nvarchar(100) = N'demo:sniff';
SELECT
    v.parent_query_id,
    v.dispatcher_plan_id,
    v.query_variant_query_id,
    p.plan_id          AS variant_plan_id,
    p.plan_type_desc,
    rs.count_executions,
    rs.avg_logical_io_reads,
    rs.avg_duration / 1000.0 AS avg_duration_ms
FROM sys.query_store_query_variant AS v
JOIN sys.query_store_plan AS p ON p.query_id = v.query_variant_query_id
JOIN sys.query_store_runtime_stats AS rs ON rs.plan_id = p.plan_id
JOIN sys.query_store_query AS parent ON parent.query_id = v.parent_query_id
CROSS APPLY sys.dm_exec_sql_text(parent.last_compile_batch_sql_handle) AS bt
WHERE bt.text LIKE N'%-- ' + @tag + N'%'
ORDER BY v.parent_query_id, p.plan_id;
GO

-- C. The DBA-side intervention: apply a hint to a query_id without touching the application.
--    Replace 0 with the query_id from A. Hints survive plan cache clears and are exempt from Query Store cleanup.
-- EXEC sys.sp_query_store_set_hints @query_id = 0, @query_hints = N'OPTION (RECOMPILE)';
-- EXEC sys.sp_query_store_set_hints @query_id = 0, @query_hints = N'OPTION (OPTIMIZE FOR UNKNOWN)';
-- EXEC sys.sp_query_store_set_hints @query_id = 0, @query_hints = N'OPTION (USE HINT(''DISABLE_PARAMETER_SNIFFING''))';
-- SQL 2025 only, the nuclear option for a query that must not run again:
-- EXEC sys.sp_query_store_set_hints @query_id = 0, @query_hints = N'OPTION (USE HINT(''ABORT_QUERY_EXECUTION''))';

-- See what hints are in place and whether any failed to apply.
SELECT query_hint_id, query_id, query_hint_text, source_desc, query_hint_failure_count, last_query_hint_failure_reason_desc
FROM sys.query_store_query_hints;

-- Remove.
-- EXEC sys.sp_query_store_clear_hints @query_id = 0;
GO

-- D. Regressed queries, the view from the old deck's Query Store slide, as T-SQL. Last hour, by average logical reads.
;WITH recent AS (
    SELECT p.query_id, p.plan_id, rs.avg_logical_io_reads, rs.count_executions, rsi.start_time
    FROM sys.query_store_plan AS p
    JOIN sys.query_store_runtime_stats AS rs ON rs.plan_id = p.plan_id
    JOIN sys.query_store_runtime_stats_interval AS rsi ON rsi.runtime_stats_interval_id = rs.runtime_stats_interval_id
    WHERE rsi.start_time > DATEADD(HOUR, -1, SYSUTCDATETIME())
)
SELECT
    r.query_id,
    COUNT(DISTINCT r.plan_id)        AS plans_in_window,
    MIN(r.avg_logical_io_reads)      AS best_avg_reads,
    MAX(r.avg_logical_io_reads)      AS worst_avg_reads,
    MAX(r.avg_logical_io_reads) / NULLIF(MIN(r.avg_logical_io_reads), 0) AS regression_factor,
    LEFT(qt.query_sql_text, 120)     AS sql_preview
FROM recent AS r
JOIN sys.query_store_query AS q ON q.query_id = r.query_id
JOIN sys.query_store_query_text AS qt ON qt.query_text_id = q.query_text_id
CROSS APPLY sys.dm_exec_sql_text(q.last_compile_batch_sql_handle) AS bt
WHERE bt.text LIKE N'%-- demo:%'
GROUP BY r.query_id, qt.query_sql_text
HAVING COUNT(DISTINCT r.plan_id) > 1
ORDER BY regression_factor DESC;
GO
