namespace LinqToQueryPlan.Data.Entities;

// Table: Teams. PK (yearID, lgID, teamID). Clustered index is (yearID, teamID), non-unique.
public class Team
{
    public short YearId { get; set; }
    public required string LgId { get; set; }
    public required string TeamId { get; set; }
    public string? FranchId { get; set; }
    public string? DivId { get; set; }
    public byte? Rank { get; set; }
    public short? G { get; set; }
    public short? W { get; set; }
    public short? L { get; set; }
    public string? DivWin { get; set; }
    public string? WcWin { get; set; }
    public string? LgWin { get; set; }
    public string? WsWin { get; set; }
    public short? R { get; set; }
    public short? Hr { get; set; }
    public double? Era { get; set; }
    public string? Name { get; set; }
    public string? Park { get; set; }
    public int? Attendance { get; set; }

    public TeamFranchise? Franchise { get; set; }
    public ICollection<Batting> Batting { get; set; } = [];
    public ICollection<Pitching> Pitching { get; set; } = [];
    public ICollection<Fielding> Fielding { get; set; } = [];
}
