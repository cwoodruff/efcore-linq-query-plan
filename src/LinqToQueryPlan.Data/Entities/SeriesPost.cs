namespace LinqToQueryPlan.Data.Entities;

// Table: SeriesPost. PK (yearID, round, teamIDwinner, lgIDwinner).
public class SeriesPost
{
    public short YearId { get; set; }
    public required string Round { get; set; }
    public required string TeamIdWinner { get; set; }
    public required string LgIdWinner { get; set; }
    public string? TeamIdLoser { get; set; }
    public string? LgIdLoser { get; set; }
    public short? Wins { get; set; }
    public short? Losses { get; set; }
    public short? Ties { get; set; }

    public Team? Winner { get; set; }
}
