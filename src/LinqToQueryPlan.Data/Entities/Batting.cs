namespace LinqToQueryPlan.Data.Entities;

// Table: Batting. PK (playerID, yearID, stint, teamID, lgID) nonclustered unique.
// Clustered index is (playerID, yearID, stint), non-unique: key lookups go through it plus a uniquifier.
public class Batting
{
    public required string PlayerId { get; set; }
    public short YearId { get; set; }
    public byte Stint { get; set; }
    public required string TeamId { get; set; }
    public required string LgId { get; set; }
    public short? G { get; set; }
    public short? Ab { get; set; }
    public short? R { get; set; }
    public short? H { get; set; }
    public short? Doubles { get; set; }   // column 2B
    public short? Triples { get; set; }   // column 3B
    public short? Hr { get; set; }
    public short? Rbi { get; set; }
    public short? Sb { get; set; }
    public short? Cs { get; set; }
    public short? Bb { get; set; }
    public short? So { get; set; }
    public short? Ibb { get; set; }
    public short? Hbp { get; set; }
    public short? Sh { get; set; }
    public short? Sf { get; set; }
    public short? Gidp { get; set; }

    public Person Player { get; set; } = null!;
    public Team Team { get; set; } = null!;
}
