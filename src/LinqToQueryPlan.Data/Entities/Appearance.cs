namespace LinqToQueryPlan.Data.Entities;

// Table: Appearances. PK (yearID, teamID, lgID, playerID).
public class Appearance
{
    public short YearId { get; set; }
    public required string TeamId { get; set; }
    public required string LgId { get; set; }
    public required string PlayerId { get; set; }
    public short? GAll { get; set; }
    public short? Gs { get; set; }
    public short? GBatting { get; set; }
    public short? GDefense { get; set; }
    public short? GP { get; set; }
    public short? GC { get; set; }
    public short? GDh { get; set; }
    public short? GPh { get; set; }

    public Person Player { get; set; } = null!;
    public Team Team { get; set; } = null!;
}
