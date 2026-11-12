namespace LinqToQueryPlan.Data.Entities;

// Table: People. PK is playerID (natural key); the int ID column exists but is not the key.
public class Person
{
    public int Id { get; set; }
    public required string PlayerId { get; set; }
    public int? BirthYear { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthDay { get; set; }
    public string? BirthCity { get; set; }
    public string? BirthCountry { get; set; }
    public string? BirthState { get; set; }
    public int? DeathYear { get; set; }
    public string? NameFirst { get; set; }
    public string? NameLast { get; set; }
    public string? NameGiven { get; set; }
    public string? Bats { get; set; }
    public string? Throws { get; set; }
    public string? Debut { get; set; }
    public string? FinalGame { get; set; }
    public string? BbrefId { get; set; }
    public string? RetroId { get; set; }

    public ICollection<Batting> Batting { get; set; } = [];
    public ICollection<Pitching> Pitching { get; set; } = [];
    public ICollection<Fielding> Fielding { get; set; } = [];
    public ICollection<Appearance> Appearances { get; set; } = [];
}
