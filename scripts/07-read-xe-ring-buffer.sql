-- 07-read-xe-ring-buffer.sql
-- Reads the LinqToQueryPlan Extended Events session from its ring buffer with plain T-SQL.
-- Works in any tool (VS Code MSSQL extension, Rider, sqlcmd). No SSMS live viewer needed.
--
-- Two result sets:
--   A. rpc_completed / sql_statement_completed: what EF actually sent, with the real parameter values your log redacts.
--   B. PSP and OPPO events: did the optimizer engage, and if not, why. Raw <data> XML is included so no field name is guessed.
--
-- For a live, scrolling view of result set A, use VS Code: MSSQL > Query Profiler > Standard_OnPrem template,
-- then filter the ApplicationName column to LinqToQueryPlan. That template does not carry the PSP/OPPO events; this script does.

;WITH xe AS (
    SELECT CAST(t.target_data AS xml) AS td
    FROM sys.dm_xe_sessions AS s
    JOIN sys.dm_xe_session_targets AS t ON t.event_session_address = s.address
    WHERE s.name = N'LinqToQueryPlan' AND t.target_name = N'ring_buffer'
),
events AS (
    SELECT
        e.n.value('@name', 'sysname')                                        AS event_name,
        e.n.value('@timestamp', 'datetime2(3)')                              AS ts_utc,
        e.n.value('(action[@name="client_app_name"]/value)[1]', 'nvarchar(128)') AS app_name,
        e.n.value('(action[@name="sql_text"]/value)[1]', 'nvarchar(max)')    AS sql_text,
        e.n.value('(data[@name="statement"]/value)[1]', 'nvarchar(max)')     AS statement,
        e.n.value('(data[@name="duration"]/value)[1]', 'bigint')             AS duration_us,
        e.n.value('(data[@name="logical_reads"]/value)[1]', 'bigint')        AS logical_reads,
        e.n.value('(data[@name="row_count"]/value)[1]', 'bigint')            AS row_count,
        e.n.query('data')                                                    AS data_xml
    FROM xe
    CROSS APPLY td.nodes('RingBufferTarget/event') AS e(n)
)
SELECT 'A. statements' AS result_set, ts_utc, event_name, duration_us / 1000.0 AS duration_ms, logical_reads, row_count,
       LEFT(COALESCE(statement, sql_text), 400) AS text_preview
FROM events
WHERE event_name IN (N'rpc_completed', N'sql_statement_completed')
ORDER BY ts_utc DESC;

;WITH xe AS (
    SELECT CAST(t.target_data AS xml) AS td
    FROM sys.dm_xe_sessions AS s
    JOIN sys.dm_xe_session_targets AS t ON t.event_session_address = s.address
    WHERE s.name = N'LinqToQueryPlan' AND t.target_name = N'ring_buffer'
)
SELECT 'B. optimizer' AS result_set,
       e.n.value('@timestamp', 'datetime2(3)')                              AS ts_utc,
       e.n.value('@name', 'sysname')                                        AS event_name,
       LEFT(e.n.value('(action[@name="sql_text"]/value)[1]', 'nvarchar(max)'), 200) AS sql_preview,
       e.n.query('data')                                                    AS data_xml
FROM xe
CROSS APPLY td.nodes('RingBufferTarget/event') AS e(n)
WHERE e.n.value('@name', 'sysname') IN (
    N'query_with_parameter_sensitivity',
    N'parameter_sensitive_plan_optimization_skipped_reason',
    N'query_with_optional_parameter_predicate',
    N'optional_parameter_optimization_skipped_reason')
ORDER BY ts_utc DESC;
GO
