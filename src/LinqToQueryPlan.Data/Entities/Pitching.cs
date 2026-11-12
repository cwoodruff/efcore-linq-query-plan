namespace LinqToQueryPlan.Data.Entities;

// Table: Pitching. Same key shape as Batting.
public class Pitching
{
    public required string PlayerId { get; set; }
    public short YearId { get; set; }
    public byte Stint { get; set; }
    public required string TeamId { get; set; }
    public required string LgId { get; set; }
    public short? W { get; set; }
    public short? L { get; set; }
    public short? G { get; set; }
    public short? Gs { get; set; }
    public short? Cg { get; set; }
    public short? Sho { get; set; }
    public short? Sv { get; set; }
    public short? IpOuts { get; set; }
    public short? H { get; set; }
    public short? Er { get; set; }
    public short? Hr { get; set; }
    public short? Bb { get; set; }
    public short? So { get; set; }
    public double? BaOpp { get; set; }
    public double? Era { get; set; }
    public short? Wp { get; set; }
    public short? Hbp { get; set; }
    public short? Bk { get; set; }
    public short? Bfp { get; set; }
    public short? Gf { get; set; }
    public short? R { get; set; }

    public Person Player { get; set; } = null!;
    public Team Team { get; set; } = null!;
}
