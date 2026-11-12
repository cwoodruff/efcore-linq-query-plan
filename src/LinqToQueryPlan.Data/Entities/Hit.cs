namespace LinqToQueryPlan.Data.Entities;

// Table: Hits. Synthetic, created by scripts/06-inflate-for-psp.sql: one row per hit from Batting.
// Exists only so the Parameter Sensitive Plan demo has enough rows and enough skew on teamID (indexed, not covering).
// Without it, demo 1 falls back to Batting.teamID.
public class Hit
{
    public long HitId { get; set; }
    public required string PlayerId { get; set; }
    public short YearId { get; set; }
    public required string TeamId { get; set; }
    public required string LgId { get; set; }
    public short HitNo { get; set; }

    public Person Player { get; set; } = null!;
}
