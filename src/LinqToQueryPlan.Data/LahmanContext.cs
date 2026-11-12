using LinqToQueryPlan.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace LinqToQueryPlan.Data;

// Hand-mapped against the lahman2025 schema (see scripts/00-environment-check.sql output).
// Every string column carries its real max length so EF declares parameters as nvarchar(n), not nvarchar(4000):
// parameter type and column type matching is part of the plan the optimizer sees.
public class LahmanContext(DbContextOptions<LahmanContext> options) : DbContext(options)
{
    public DbSet<Person> People => Set<Person>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamFranchise> TeamFranchises => Set<TeamFranchise>();
    public DbSet<Batting> Batting => Set<Batting>();
    public DbSet<Pitching> Pitching => Set<Pitching>();
    public DbSet<Fielding> Fielding => Set<Fielding>();
    public DbSet<Appearance> Appearances => Set<Appearance>();
    public DbSet<SeriesPost> SeriesPost => Set<SeriesPost>();
    public DbSet<Hit> Hits => Set<Hit>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        mb.Entity<Person>(e =>
        {
            e.ToTable("People");
            e.HasKey(p => p.PlayerId);
            e.Property(p => p.Id).HasColumnName("ID");
            e.Property(p => p.PlayerId).HasColumnName("playerID").HasMaxLength(20);
            e.Property(p => p.BirthYear).HasColumnName("birthYear");
            e.Property(p => p.BirthMonth).HasColumnName("birthMonth");
            e.Property(p => p.BirthDay).HasColumnName("birthDay");
            e.Property(p => p.BirthCity).HasColumnName("birthCity").HasMaxLength(100);
            e.Property(p => p.BirthCountry).HasColumnName("birthCountry").HasMaxLength(50);
            e.Property(p => p.BirthState).HasColumnName("birthState").HasMaxLength(50);
            e.Property(p => p.DeathYear).HasColumnName("deathYear");
            e.Property(p => p.NameFirst).HasColumnName("nameFirst").HasMaxLength(100);
            e.Property(p => p.NameLast).HasColumnName("nameLast").HasMaxLength(100);
            e.Property(p => p.NameGiven).HasColumnName("nameGiven").HasMaxLength(100);
            e.Property(p => p.Bats).HasColumnName("bats").HasMaxLength(10);
            e.Property(p => p.Throws).HasColumnName("throws").HasMaxLength(10);
            e.Property(p => p.Debut).HasColumnName("debut").HasMaxLength(20);
            e.Property(p => p.FinalGame).HasColumnName("finalGame").HasMaxLength(20);
            e.Property(p => p.BbrefId).HasColumnName("bbrefID").HasMaxLength(20);
            e.Property(p => p.RetroId).HasColumnName("retroID").HasMaxLength(20);
        });

        mb.Entity<TeamFranchise>(e =>
        {
            e.ToTable("TeamsFranchises");
            e.HasKey(f => f.FranchId);
            e.Property(f => f.FranchId).HasColumnName("franchID").HasMaxLength(4);
            e.Property(f => f.FranchName).HasColumnName("franchName").HasMaxLength(100);
            e.Property(f => f.Active).HasColumnName("active").HasMaxLength(2);
            e.Property(f => f.NaAssoc).HasColumnName("NAassoc").HasMaxLength(4);
        });

        mb.Entity<Team>(e =>
        {
            e.ToTable("Teams");
            e.HasKey(t => new { t.YearId, t.LgId, t.TeamId });
            e.Property(t => t.YearId).HasColumnName("yearID");
            e.Property(t => t.LgId).HasColumnName("lgID").HasMaxLength(3);
            e.Property(t => t.TeamId).HasColumnName("teamID").HasMaxLength(4);
            e.Property(t => t.FranchId).HasColumnName("franchID").HasMaxLength(4);
            e.Property(t => t.DivId).HasColumnName("divID").HasMaxLength(2);
            e.Property(t => t.Rank).HasColumnName("Rank");
            e.Property(t => t.G).HasColumnName("G");
            e.Property(t => t.W).HasColumnName("W");
            e.Property(t => t.L).HasColumnName("L");
            e.Property(t => t.DivWin).HasColumnName("DivWin").HasMaxLength(1);
            e.Property(t => t.WcWin).HasColumnName("WCWin").HasMaxLength(1);
            e.Property(t => t.LgWin).HasColumnName("LgWin").HasMaxLength(1);
            e.Property(t => t.WsWin).HasColumnName("WSWin").HasMaxLength(1);
            e.Property(t => t.R).HasColumnName("R");
            e.Property(t => t.Hr).HasColumnName("HR");
            e.Property(t => t.Era).HasColumnName("ERA");
            e.Property(t => t.Name).HasColumnName("name").HasMaxLength(100);
            e.Property(t => t.Park).HasColumnName("park").HasMaxLength(255);
            e.Property(t => t.Attendance).HasColumnName("attendance");

            e.HasOne(t => t.Franchise).WithMany(f => f.Teams).HasForeignKey(t => t.FranchId);
        });

        mb.Entity<Batting>(e =>
        {
            e.ToTable("Batting");
            e.HasKey(b => new { b.PlayerId, b.YearId, b.Stint, b.TeamId, b.LgId });
            e.Property(b => b.PlayerId).HasColumnName("playerID").HasMaxLength(20);
            e.Property(b => b.YearId).HasColumnName("yearID");
            e.Property(b => b.Stint).HasColumnName("stint");
            e.Property(b => b.TeamId).HasColumnName("teamID").HasMaxLength(4);
            e.Property(b => b.LgId).HasColumnName("lgID").HasMaxLength(3);
            e.Property(b => b.G).HasColumnName("G");
            e.Property(b => b.Ab).HasColumnName("AB");
            e.Property(b => b.R).HasColumnName("R");
            e.Property(b => b.H).HasColumnName("H");
            e.Property(b => b.Doubles).HasColumnName("2B");
            e.Property(b => b.Triples).HasColumnName("3B");
            e.Property(b => b.Hr).HasColumnName("HR");
            e.Property(b => b.Rbi).HasColumnName("RBI");
            e.Property(b => b.Sb).HasColumnName("SB");
            e.Property(b => b.Cs).HasColumnName("CS");
            e.Property(b => b.Bb).HasColumnName("BB");
            e.Property(b => b.So).HasColumnName("SO");
            e.Property(b => b.Ibb).HasColumnName("IBB");
            e.Property(b => b.Hbp).HasColumnName("HBP");
            e.Property(b => b.Sh).HasColumnName("SH");
            e.Property(b => b.Sf).HasColumnName("SF");
            e.Property(b => b.Gidp).HasColumnName("GIDP");

            e.HasOne(b => b.Player).WithMany(p => p.Batting).HasForeignKey(b => b.PlayerId);
            e.HasOne(b => b.Team).WithMany(t => t.Batting).HasForeignKey(b => new { b.YearId, b.LgId, b.TeamId });
        });

        mb.Entity<Pitching>(e =>
        {
            e.ToTable("Pitching");
            e.HasKey(p => new { p.PlayerId, p.YearId, p.Stint, p.TeamId, p.LgId });
            e.Property(p => p.PlayerId).HasColumnName("playerID").HasMaxLength(20);
            e.Property(p => p.YearId).HasColumnName("yearID");
            e.Property(p => p.Stint).HasColumnName("stint");
            e.Property(p => p.TeamId).HasColumnName("teamID").HasMaxLength(4);
            e.Property(p => p.LgId).HasColumnName("lgID").HasMaxLength(3);
            e.Property(p => p.W).HasColumnName("W");
            e.Property(p => p.L).HasColumnName("L");
            e.Property(p => p.G).HasColumnName("G");
            e.Property(p => p.Gs).HasColumnName("GS");
            e.Property(p => p.Cg).HasColumnName("CG");
            e.Property(p => p.Sho).HasColumnName("SHO");
            e.Property(p => p.Sv).HasColumnName("SV");
            e.Property(p => p.IpOuts).HasColumnName("IPouts");
            e.Property(p => p.H).HasColumnName("H");
            e.Property(p => p.Er).HasColumnName("ER");
            e.Property(p => p.Hr).HasColumnName("HR");
            e.Property(p => p.Bb).HasColumnName("BB");
            e.Property(p => p.So).HasColumnName("SO");
            e.Property(p => p.BaOpp).HasColumnName("BAOpp");
            e.Property(p => p.Era).HasColumnName("ERA");
            e.Property(p => p.Wp).HasColumnName("WP");
            e.Property(p => p.Hbp).HasColumnName("HBP");
            e.Property(p => p.Bk).HasColumnName("BK");
            e.Property(p => p.Bfp).HasColumnName("BFP");
            e.Property(p => p.Gf).HasColumnName("GF");
            e.Property(p => p.R).HasColumnName("R");

            e.HasOne(p => p.Player).WithMany(x => x.Pitching).HasForeignKey(p => p.PlayerId);
            e.HasOne(p => p.Team).WithMany(t => t.Pitching).HasForeignKey(p => new { p.YearId, p.LgId, p.TeamId });
        });

        mb.Entity<Fielding>(e =>
        {
            e.ToTable("Fielding");
            e.HasKey(f => new { f.PlayerId, f.YearId, f.Stint, f.TeamId, f.LgId, f.Pos });
            e.Property(f => f.PlayerId).HasColumnName("playerID").HasMaxLength(20);
            e.Property(f => f.YearId).HasColumnName("yearID");
            e.Property(f => f.Stint).HasColumnName("stint");
            e.Property(f => f.TeamId).HasColumnName("teamID").HasMaxLength(4);
            e.Property(f => f.LgId).HasColumnName("lgID").HasMaxLength(3);
            e.Property(f => f.Pos).HasColumnName("POS").HasMaxLength(2);
            e.Property(f => f.G).HasColumnName("G");
            e.Property(f => f.Gs).HasColumnName("GS");
            e.Property(f => f.InnOuts).HasColumnName("InnOuts");
            e.Property(f => f.Po).HasColumnName("PO");
            e.Property(f => f.A).HasColumnName("A");
            e.Property(f => f.E).HasColumnName("E");
            e.Property(f => f.Dp).HasColumnName("DP");

            e.HasOne(f => f.Player).WithMany(x => x.Fielding).HasForeignKey(f => f.PlayerId);
            e.HasOne(f => f.Team).WithMany(t => t.Fielding).HasForeignKey(f => new { f.YearId, f.LgId, f.TeamId });
        });

        mb.Entity<Appearance>(e =>
        {
            e.ToTable("Appearances");
            e.HasKey(a => new { a.YearId, a.TeamId, a.LgId, a.PlayerId });
            e.Property(a => a.YearId).HasColumnName("yearID");
            e.Property(a => a.TeamId).HasColumnName("teamID").HasMaxLength(4);
            e.Property(a => a.LgId).HasColumnName("lgID").HasMaxLength(3);
            e.Property(a => a.PlayerId).HasColumnName("playerID").HasMaxLength(20);
            e.Property(a => a.GAll).HasColumnName("G_all");
            e.Property(a => a.Gs).HasColumnName("GS");
            e.Property(a => a.GBatting).HasColumnName("G_batting");
            e.Property(a => a.GDefense).HasColumnName("G_defense");
            e.Property(a => a.GP).HasColumnName("G_p");
            e.Property(a => a.GC).HasColumnName("G_c");
            e.Property(a => a.GDh).HasColumnName("G_dh");
            e.Property(a => a.GPh).HasColumnName("G_ph");

            e.HasOne(a => a.Player).WithMany(p => p.Appearances).HasForeignKey(a => a.PlayerId);
            e.HasOne(a => a.Team).WithMany().HasForeignKey(a => new { a.YearId, a.LgId, a.TeamId });
        });

        mb.Entity<SeriesPost>(e =>
        {
            e.ToTable("SeriesPost");
            e.HasKey(s => new { s.YearId, s.Round, s.TeamIdWinner, s.LgIdWinner });
            e.Property(s => s.YearId).HasColumnName("yearID");
            e.Property(s => s.Round).HasColumnName("round").HasMaxLength(10);
            e.Property(s => s.TeamIdWinner).HasColumnName("teamIDwinner").HasMaxLength(4);
            e.Property(s => s.LgIdWinner).HasColumnName("lgIDwinner").HasMaxLength(3);
            e.Property(s => s.TeamIdLoser).HasColumnName("teamIDloser").HasMaxLength(4);
            e.Property(s => s.LgIdLoser).HasColumnName("lgIDloser").HasMaxLength(3);
            e.Property(s => s.Wins).HasColumnName("wins");
            e.Property(s => s.Losses).HasColumnName("losses");
            e.Property(s => s.Ties).HasColumnName("ties");

            e.HasOne(s => s.Winner).WithMany().HasForeignKey(s => new { s.YearId, s.LgIdWinner, s.TeamIdWinner });
        });

        mb.Entity<Hit>(e =>
        {
            e.ToTable("Hits");
            e.HasKey(h => h.HitId);
            e.Property(h => h.HitId).HasColumnName("hitID");
            e.Property(h => h.PlayerId).HasColumnName("playerID").HasMaxLength(20);
            e.Property(h => h.YearId).HasColumnName("yearID");
            e.Property(h => h.TeamId).HasColumnName("teamID").HasMaxLength(4);
            e.Property(h => h.LgId).HasColumnName("lgID").HasMaxLength(3);
            e.Property(h => h.HitNo).HasColumnName("hitNo");

            e.HasOne(h => h.Player).WithMany().HasForeignKey(h => h.PlayerId);
        });
    }
}
