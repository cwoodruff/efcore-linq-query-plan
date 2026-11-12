namespace LinqToQueryPlan.Data.Entities;

// Table: Fielding. PK (playerID, yearID, stint, teamID, lgID, POS).
public class Fielding
{
    public required string PlayerId { get; set; }
    public short YearId { get; set; }
    public byte Stint { get; set; }
    public required string TeamId { get; set; }
    public required string LgId { get; set; }
    public required string Pos { get; set; }
    public short? G { get; set; }
    public short? Gs { get; set; }
    public short? InnOuts { get; set; }
    public short? Po { get; set; }
    public short? A { get; set; }
    public short? E { get; set; }
    public short? Dp { get; set; }

    public Person Player { get; set; } = null!;
    public Team Team { get; set; } = null!;
}
