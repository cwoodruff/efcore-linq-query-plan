-- 08-case-setup.sql
-- Laptop only. Sets the stage for the case that opens the talk: the team page that's instant for a tiny club and slow for the Cubs.
--
-- At compatibility level 170, Parameter Sensitive Plan optimization would give the Cubs their own plan and there'd be no case.
-- Plenty of production databases don't get that help: they're below level 160, or PSP declined the query. Turning PSP off
-- here makes this database behave like one of those. scripts/09 (or Setup) turns it back on, and that's the case's permanent fix.
--
-- Also clears this database's plans and Query Store, so the first value after this script compiles the plan.

USE lahman2025;
GO

ALTER DATABASE SCOPED CONFIGURATION SET PARAMETER_SENSITIVE_PLAN_OPTIMIZATION = OFF;
GO

ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
GO

ALTER DATABASE lahman2025 SET QUERY_STORE CLEAR;
GO

SELECT name, value FROM sys.database_scoped_configurations WHERE name = N'PARAMETER_SENSITIVE_PLAN_OPTIMIZATION';
GO
