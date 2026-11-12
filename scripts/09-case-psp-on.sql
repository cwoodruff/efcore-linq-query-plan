-- 09-case-psp-on.sql
-- Laptop only. The case's permanent fix, the DBA's rung: turn Parameter Sensitive Plan optimization back on.
-- In production the equivalent is usually raising the database compatibility level to 160 or 170, which is the DBA's call.
-- Clears this database's plans so the next execution compiles a PSP dispatcher instead of reusing the old plan.

USE lahman2025;
GO

ALTER DATABASE SCOPED CONFIGURATION SET PARAMETER_SENSITIVE_PLAN_OPTIMIZATION = ON;
GO

ALTER DATABASE SCOPED CONFIGURATION CLEAR PROCEDURE_CACHE;
GO

SELECT name, value FROM sys.database_scoped_configurations WHERE name = N'PARAMETER_SENSITIVE_PLAN_OPTIMIZATION';
GO
