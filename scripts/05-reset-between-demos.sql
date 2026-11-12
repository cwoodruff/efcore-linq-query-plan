-- 05-reset-between-demos.sql
-- Laptop only. Say so on the slide. Clears the plan cache and Query Store so each demo starts from nothing.
-- Never run DBCC FREEPROCCACHE against a shared or production instance.

USE lahman2025;
GO

DBCC FREEPROCCACHE;
GO

-- Optional: wipe Query Store history so the variants query shows only this demo's plans.
-- Comment out if you want section 6's regressed-queries view to accumulate across demos.
ALTER DATABASE lahman2025 SET QUERY_STORE CLEAR;
GO

-- Restart the XE ring buffer so it only holds this demo's events. Skipped if script 03 hasn't created the session yet.
IF EXISTS (SELECT 1 FROM sys.server_event_sessions WHERE name = N'LinqToQueryPlan')
BEGIN
    IF EXISTS (SELECT 1 FROM sys.dm_xe_sessions WHERE name = N'LinqToQueryPlan')
        ALTER EVENT SESSION [LinqToQueryPlan] ON SERVER STATE = STOP;
    ALTER EVENT SESSION [LinqToQueryPlan] ON SERVER STATE = START;
END
ELSE
    PRINT 'XE session LinqToQueryPlan does not exist yet; run scripts/03-xe-session.sql.';
GO
