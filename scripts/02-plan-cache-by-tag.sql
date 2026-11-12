-- 02-plan-cache-by-tag.sql
-- Section 4 (Contains in three modes) and section 3 (sniffing).
-- Every demo query carries a TagWith("demo:<name>") comment on its first line, so this groups cached plans by that tag.
-- In Constant mode each distinct list is a different SQL text and a different plan: the count climbs with every call.
-- In Parameter (OPENJSON) mode the count stays at 1.
-- In MultipleParameters mode the count is the number of padding buckets you have hit.

USE lahman2025;
GO

DECLARE @tag nvarchar(100) = N'demo:contains';   -- change per demo

;WITH tagged AS (
    SELECT
        cp.plan_handle,
        cp.usecounts,
        cp.size_in_bytes,
        cp.objtype,
        st.text,
        SUBSTRING(st.text, CHARINDEX(N'-- ' + @tag, st.text), 60) AS tag_line
    FROM sys.dm_exec_cached_plans AS cp
    CROSS APPLY sys.dm_exec_sql_text(cp.plan_handle) AS st
    WHERE st.text LIKE N'%-- ' + @tag + N'%'
      AND st.text NOT LIKE N'%dm_exec_cached_plans%'
)
SELECT
    tag_line,
    COUNT(*)            AS cached_plans,
    SUM(usecounts)      AS total_uses,
    SUM(size_in_bytes) / 1024 AS total_kb,
    MIN(LEN(text))      AS shortest_sql_chars,
    MAX(LEN(text))      AS longest_sql_chars
FROM tagged
GROUP BY tag_line
ORDER BY cached_plans DESC;

-- Drill into the individual texts when the count surprises you.
-- SELECT usecounts, LEFT(text, 400) AS sql_text FROM tagged ORDER BY usecounts DESC;
GO

-- Section 2: the sniffing tell. Compiled vs runtime parameter values live in the plan XML.
-- Run after the sniffing demo to pull them without opening the graphical plan.
DECLARE @tag2 nvarchar(100) = N'demo:sniff';

;WITH XMLNAMESPACES (DEFAULT 'http://schemas.microsoft.com/sqlserver/2004/07/showplan')
SELECT
    qs.execution_count,
    qs.total_logical_reads / NULLIF(qs.execution_count, 0) AS avg_logical_reads,
    p.n.value('@Column', 'nvarchar(100)')               AS parameter_name,
    p.n.value('@ParameterCompiledValue', 'nvarchar(100)') AS compiled_value,
    p.n.value('@ParameterRuntimeValue', 'nvarchar(100)')  AS runtime_value
FROM sys.dm_exec_query_stats AS qs
CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) AS st
CROSS APPLY sys.dm_exec_query_plan(qs.plan_handle) AS qp
CROSS APPLY qp.query_plan.nodes('//ParameterList/ColumnReference') AS p(n)
WHERE st.text LIKE N'%-- ' + @tag2 + N'%'
  AND st.text NOT LIKE N'%dm_exec_query_stats%';
GO
