-- 00-environment-check.sql
-- Run against the lahman2025 container from VS Code (MSSQL extension), Rider, or sqlcmd. Read-only except for nothing: it changes no settings.
-- Paste the full output back so the demo solution is built against your real schema and engine.

USE lahman2025;
GO

PRINT '=== 1. Engine version (need SQL Server 2025 / 17.x for compat 170, OPPO, PSP on DML) ===';
SELECT
    SERVERPROPERTY('ProductVersion')      AS ProductVersion,
    SERVERPROPERTY('ProductLevel')        AS ProductLevel,
    SERVERPROPERTY('ProductUpdateLevel')  AS CU,
    SERVERPROPERTY('Edition')             AS Edition;
GO

PRINT '=== 2. Database compatibility level and Query Store state ===';
SELECT
    d.name,
    d.compatibility_level,
    qs.actual_state_desc        AS query_store_state,
    qs.query_capture_mode_desc  AS capture_mode
FROM sys.databases AS d
LEFT JOIN sys.database_query_store_options AS qs ON 1 = 1
WHERE d.name = DB_NAME();
GO

PRINT '=== 3. Optimizer features that matter for the talk (database scoped configuration) ===';
SELECT name, value, value_for_secondary
FROM sys.database_scoped_configurations
WHERE name IN (
    'PARAMETER_SENSITIVE_PLAN_OPTIMIZATION',
    'OPTIONAL_PARAMETER_OPTIMIZATION',
    'OPTIMIZED_SP_EXECUTESQL',
    'PARAMETER_SNIFFING',
    'DOP_FEEDBACK',
    'CE_FEEDBACK',
    'LEGACY_CARDINALITY_ESTIMATION',
    'OPTIMIZED_PLAN_FORCING'
)
ORDER BY name;
GO

PRINT '=== 4. Row counts for the tables the demos will use ===';
SELECT t.name AS table_name, SUM(p.rows) AS row_count
FROM sys.tables AS t
JOIN sys.partitions AS p ON p.object_id = t.object_id AND p.index_id IN (0, 1)
WHERE t.name IN ('People','Batting','Pitching','Fielding','Teams','TeamsFranchises',
                 'SeriesPost','Appearances','AllstarFull','Salaries','Managers','HallOfFame')
GROUP BY t.name
ORDER BY row_count DESC;
GO

PRINT '=== 5. Columns and primary keys for the demo tables (so the EF model matches exactly) ===';
SELECT
    c.TABLE_NAME,
    c.ORDINAL_POSITION,
    c.COLUMN_NAME,
    c.DATA_TYPE,
    c.CHARACTER_MAXIMUM_LENGTH,
    c.IS_NULLABLE,
    CASE WHEN k.COLUMN_NAME IS NOT NULL THEN 'PK' ELSE '' END AS key_part
FROM INFORMATION_SCHEMA.COLUMNS AS c
LEFT JOIN (
    SELECT ku.TABLE_NAME, ku.COLUMN_NAME
    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
    JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS ku
      ON tc.CONSTRAINT_NAME = ku.CONSTRAINT_NAME AND tc.TABLE_NAME = ku.TABLE_NAME
    WHERE tc.CONSTRAINT_TYPE = 'PRIMARY KEY'
) AS k ON k.TABLE_NAME = c.TABLE_NAME AND k.COLUMN_NAME = c.COLUMN_NAME
WHERE c.TABLE_NAME IN ('People','Batting','Pitching','Fielding','Teams','TeamsFranchises','SeriesPost','Appearances')
ORDER BY c.TABLE_NAME, c.ORDINAL_POSITION;
GO

PRINT '=== 6. Existing indexes on the demo tables (PSP needs an index the skewed predicate can seek on) ===';
SELECT
    OBJECT_NAME(i.object_id) AS table_name,
    i.name                   AS index_name,
    i.type_desc,
    i.is_unique,
    STRING_AGG(COL_NAME(ic.object_id, ic.column_id), ', ') WITHIN GROUP (ORDER BY ic.key_ordinal) AS key_columns
FROM sys.indexes AS i
JOIN sys.index_columns AS ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id AND ic.is_included_column = 0
WHERE OBJECT_NAME(i.object_id) IN ('People','Batting','Pitching','Fielding','Teams','SeriesPost','Appearances')
  AND i.type > 0
GROUP BY i.object_id, i.name, i.type_desc, i.is_unique
ORDER BY table_name, index_name;
GO

PRINT '=== 7. Skew check: candidate columns for the parameter sniffing / PSP demo ===';
PRINT '    Higher max_to_avg means more skew. PSP wants equality predicates on skewed columns with usable statistics.';
WITH skew AS (
    SELECT 'Batting'   AS tbl, 'teamID'   AS col, COUNT(*) AS n FROM Batting   GROUP BY teamID
    UNION ALL SELECT 'Batting',   'yearID',   COUNT(*) FROM Batting   GROUP BY yearID
    UNION ALL SELECT 'Batting',   'playerID', COUNT(*) FROM Batting   GROUP BY playerID
    UNION ALL SELECT 'Pitching',  'teamID',   COUNT(*) FROM Pitching  GROUP BY teamID
    UNION ALL SELECT 'Pitching',  'yearID',   COUNT(*) FROM Pitching  GROUP BY yearID
    UNION ALL SELECT 'Fielding',  'POS',      COUNT(*) FROM Fielding  GROUP BY POS
    UNION ALL SELECT 'Fielding',  'teamID',   COUNT(*) FROM Fielding  GROUP BY teamID
    UNION ALL SELECT 'Appearances','teamID',  COUNT(*) FROM Appearances GROUP BY teamID
    UNION ALL SELECT 'People',    'birthCountry', COUNT(*) FROM People GROUP BY birthCountry
    UNION ALL SELECT 'People',    'birthState',   COUNT(*) FROM People GROUP BY birthState
)
SELECT
    tbl, col,
    COUNT(*)      AS distinct_values,
    MIN(n)        AS min_rows_per_value,
    AVG(n)        AS avg_rows_per_value,
    MAX(n)        AS max_rows_per_value,
    CAST(MAX(n) AS decimal(18,2)) / NULLIF(AVG(n), 0) AS max_to_avg
FROM skew
GROUP BY tbl, col
ORDER BY max_to_avg DESC;
GO

PRINT '=== 8. Has anything already produced PSP variants in Query Store? (empty is fine) ===';
SELECT TOP (10)
    v.query_variant_query_id,
    v.parent_query_id,
    v.dispatcher_plan_id
FROM sys.query_store_query_variant AS v;
GO
