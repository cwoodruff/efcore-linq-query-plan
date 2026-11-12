-- 03-xe-session.sql
-- Section 6 (watching it happen) and section 3 (why PSP engaged or skipped).
-- Replaces the old deck's Profiler slide and the ChinookAlbumQueries session.
-- Filter is on client_app_name. The demo app sets Application Name=LinqToQueryPlan in its connection string;
-- without that, EF 10 injects its own anonymous version string, which is the slide 6 side-note.

USE master;
GO

IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'LinqToQueryPlan')
    DROP EVENT SESSION [LinqToQueryPlan] ON SERVER;
GO

CREATE EVENT SESSION [LinqToQueryPlan] ON SERVER
    -- What EF actually sent. rpc_completed is the sp_executesql call, with the real parameter values your log redacts.
    ADD EVENT sqlserver.rpc_completed (
        ACTION (sqlserver.sql_text, sqlserver.client_app_name, sqlserver.database_name, sqlserver.query_hash, sqlserver.query_plan_hash)
        WHERE sqlserver.client_app_name = N'LinqToQueryPlan'),
    ADD EVENT sqlserver.sql_statement_completed (
        ACTION (sqlserver.sql_text, sqlserver.client_app_name, sqlserver.query_hash, sqlserver.query_plan_hash)
        WHERE sqlserver.client_app_name = N'LinqToQueryPlan'),

    -- Parameter Sensitive Plan optimization: did it engage, and if not, why.
    ADD EVENT sqlserver.query_with_parameter_sensitivity (
        ACTION (sqlserver.sql_text, sqlserver.client_app_name)
        WHERE sqlserver.client_app_name = N'LinqToQueryPlan'),
    ADD EVENT sqlserver.parameter_sensitive_plan_optimization_skipped_reason (
        ACTION (sqlserver.sql_text, sqlserver.client_app_name)
        WHERE sqlserver.client_app_name = N'LinqToQueryPlan'),

    -- Optional Parameter Plan Optimization (SQL 2025): same pair of events.
    ADD EVENT sqlserver.query_with_optional_parameter_predicate (
        ACTION (sqlserver.sql_text, sqlserver.client_app_name)
        WHERE sqlserver.client_app_name = N'LinqToQueryPlan'),
    ADD EVENT sqlserver.optional_parameter_optimization_skipped_reason (
        ACTION (sqlserver.sql_text, sqlserver.client_app_name)
        WHERE sqlserver.client_app_name = N'LinqToQueryPlan')

    ADD TARGET package0.ring_buffer (SET max_memory = 4096)
    WITH (MAX_MEMORY = 8192 KB, EVENT_RETENTION_MODE = ALLOW_SINGLE_EVENT_LOSS,
          MAX_DISPATCH_LATENCY = 5 SECONDS, TRACK_CAUSALITY = ON, STARTUP_STATE = OFF);
GO

ALTER EVENT SESSION [LinqToQueryPlan] ON SERVER STATE = START;
GO

-- Read it with scripts/07-read-xe-ring-buffer.sql (any tool). For a live scrolling view of rpc_completed on macOS,
-- use VS Code: MSSQL > Query Profiler > Standard_OnPrem, filter ApplicationName = LinqToQueryPlan.
-- Decode the PSP and OPPO skip reasons:
PRINT 'PSP skip reason codes:';
SELECT map_key, map_value FROM sys.dm_xe_map_values WHERE name = N'psp_skipped_reason_enum' ORDER BY map_key;
PRINT 'OPPO skip reason codes:';
SELECT map_key, map_value FROM sys.dm_xe_map_values WHERE name = N'opo_skipped_reason_enum' ORDER BY map_key;
GO

-- Stop between demos so the ring buffer stays readable:
-- ALTER EVENT SESSION [LinqToQueryPlan] ON SERVER STATE = STOP;
