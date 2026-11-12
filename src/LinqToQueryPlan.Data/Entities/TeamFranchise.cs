namespace LinqToQueryPlan.Data.Entities;

// Table: TeamsFranchises. PK franchID.
public class TeamFranchise
{
    public required string FranchId { get; set; }
    public string? FranchName { get; set; }
    public string? Active { get; set; }
    public string? NaAssoc { get; set; }

    public ICollection<Team> Teams { get; set; } = [];
}
