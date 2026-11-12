-- 06-inflate-for-psp.sql
-- Optional. Builds dbo.Hits: one row per hit in Batting, so the Parameter Sensitive Plan demo (demo 3) has
-- enough rows and enough skew. Several million rows; per team, from a handful of hits to hundreds of thousands.
-- Demo 3 runs on Batting.teamID without this. On Batting, PSP usually declines (XE reports SkewnessThresholdNotMet);
-- on Hits it builds separate variants for rare and common teams.
-- Takes a minute or two in a container. Re-runnable: drops and rebuilds.

USE lahman2025;
GO

IF OBJECT_ID('dbo.Hits') IS NOT NULL DROP TABLE dbo.Hits;
GO

CREATE TABLE dbo.Hits (
    hitID    bigint IDENTITY(1,1) NOT NULL,
    playerID nvarchar(20) NOT NULL,
    yearID   smallint     NOT NULL,
    teamID   nvarchar(4)  NOT NULL,
    lgID     nvarchar(3)  NOT NULL,
    hitNo    smallint     NOT NULL,
    CONSTRAINT PK_Hits PRIMARY KEY CLUSTERED (hitID)
);
GO

-- Numbers 1..300 (no season has more than 262 hits by one player).
;WITH n AS (
    SELECT TOP (300) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS num
    FROM sys.all_columns AS a CROSS JOIN sys.all_columns AS b
)
INSERT INTO dbo.Hits (playerID, yearID, teamID, lgID, hitNo)
SELECT b.playerID, b.yearID, b.teamID, b.lgID, CAST(n.num AS smallint)
FROM dbo.Batting AS b
JOIN n ON n.num <= b.H
WHERE b.H > 0
ORDER BY b.playerID, b.yearID, b.stint, n.num;
GO

-- The index the sniffing demo seeks on. Deliberately NOT covering: the rare team's plan is a seek plus a key lookup
-- per row, the common team's best plan is a scan, and that gap is what parameter sniffing (and PSP) is about.
-- A covering index would make a seek right for every value and there'd be nothing to demo.
CREATE NONCLUSTERED INDEX IX_Hits_team ON dbo.Hits (teamID);
GO

UPDATE STATISTICS dbo.Hits WITH FULLSCAN;
GO

-- Sanity: skew by team. Demo 3 picks the rarest and most common team from the data at runtime.
SELECT COUNT(*) AS total_hits, COUNT(DISTINCT teamID) AS teams FROM dbo.Hits;
SELECT TOP (5) teamID, COUNT(*) AS hits FROM dbo.Hits GROUP BY teamID ORDER BY hits DESC;
SELECT TOP (5) teamID, COUNT(*) AS hits FROM dbo.Hits GROUP BY teamID ORDER BY hits ASC;
GO
