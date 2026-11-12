-- 01-setup-database.sql
-- Run once against the lahman2025 container before the demos. Idempotent.
-- Everything here is also a demo moment for section 1 (the two compatibility levels):
-- EF's UseCompatibilityLevel() decides what SQL EF writes; this script decides what the engine will do with it.

USE master;
GO

-- 1. Compatibility level 170 = SQL Server 2025. Required for OPPO and the 2025 PSP extensions.
--    A database restored from an older backup keeps its old level, so this is not optional.
ALTER DATABASE lahman2025 SET COMPATIBILITY_LEVEL = 170;
GO

USE lahman2025;
GO

-- 2. Query Store. On by default for new databases since 2022, but a restored backup carries whatever it had.
--    QUERY_CAPTURE_MODE = ALL so cheap, infrequent demo queries are captured (AUTO skips them).
ALTER DATABASE lahman2025 SET QUERY_STORE = ON (
    OPERATION_MODE = READ_WRITE,
    QUERY_CAPTURE_MODE = ALL,
    DATA_FLUSH_INTERVAL_SECONDS = 60,
    INTERVAL_LENGTH_MINUTES = 1,
    MAX_STORAGE_SIZE_MB = 500
);
GO

-- 3. Optimizer features the talk depends on. All default ON at compat 170 except OPTIMIZED_SP_EXECUTESQL.
ALTER DATABASE SCOPED CONFIGURATION SET PARAMETER_SENSITIVE_PLAN_OPTIMIZATION = ON;
ALTER DATABASE SCOPED CONFIGURATION SET OPTIONAL_PARAMETER_OPTIMIZATION = ON;
ALTER DATABASE SCOPED CONFIGURATION SET PARAMETER_SNIFFING = ON;   -- PSP is disabled if sniffing is off
ALTER DATABASE SCOPED CONFIGURATION SET DOP_FEEDBACK = ON;
ALTER DATABASE SCOPED CONFIGURATION SET CE_FEEDBACK = ON;
GO

-- 3b. Keep the last actual plan for every cached plan (sys.dm_exec_query_plan_stats).
--     The web app uses it to show actual rows per operator for the exact plan EF ran, without pasting SQL anywhere.
--     Pasting ToQueryString() output into a query window is not the same thing: it declares the values as local variables,
--     and the optimizer doesn't sniff local variables. You'd be looking at a different plan.
ALTER DATABASE SCOPED CONFIGURATION SET LAST_QUERY_PLAN_STATS = ON;
GO

-- 4. Section 7, rung 4. Leave OFF for the main demos; flip ON only for the compile-storm slide.
--    Every EF query arrives via sp_executesql, so this is the one engine knob built for EF-shaped workloads.
--    Microsoft recommends async stats updates alongside it.
-- ALTER DATABASE lahman2025 SET AUTO_UPDATE_STATISTICS_ASYNC ON;
-- ALTER DATABASE SCOPED CONFIGURATION SET ASYNC_STATS_UPDATE_WAIT_AT_LOW_PRIORITY = ON;
-- ALTER DATABASE SCOPED CONFIGURATION SET OPTIMIZED_SP_EXECUTESQL = ON;
GO

-- 5. Confirm.
SELECT name, compatibility_level FROM sys.databases WHERE name = 'lahman2025';
SELECT name, value FROM sys.database_scoped_configurations
WHERE name IN ('PARAMETER_SENSITIVE_PLAN_OPTIMIZATION','OPTIONAL_PARAMETER_OPTIMIZATION','OPTIMIZED_SP_EXECUTESQL','PARAMETER_SNIFFING','LAST_QUERY_PLAN_STATS');
SELECT actual_state_desc, query_capture_mode_desc FROM sys.database_query_store_options;
GO
