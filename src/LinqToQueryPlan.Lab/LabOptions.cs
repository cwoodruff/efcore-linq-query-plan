using Microsoft.Data.SqlClient;

namespace LinqToQueryPlan.Lab;

public sealed class LabOptions
{
    public const string Section = "Lab";

    // Demo queries run under this application name; scripts/03-xe-session.sql filters on it.
    public const string DemoAppName = "LinqToQueryPlan";

    // DMV, Query Store, and XE reads run under a different name so they never show up in the demo trace.
    public const string DiagnosticsAppName = "LinqToQueryPlan.Diagnostics";

    // 127.0.0.1 rather than localhost: on macOS, "localhost" can resolve to ::1 first, and Docker Desktop's port
    // forward doesn't always answer there. Encrypt=False: a local container has no certificate worth validating,
    // and skipping TLS removes a handshake that has been known to stall on macOS.
    public string ConnectionString { get; set; } =
        "Server=127.0.0.1,1433;Database=lahman2025;User Id=sa;Password=8riwudeg!!;Encrypt=False;TrustServerCertificate=True";

    public string DatabaseName { get; set; } = "lahman2025";

    // Lets the app run scripts 01, 03, 05, 06 and Query Store hint procedures. Development only, and only against a local server.
    public bool AllowAdminActions { get; set; }

    // Extra host names (besides localhost, 127.0.0.1, ::1) that admin actions may target.
    public List<string> AdminHosts { get; set; } = [];

    // Folder that holds the numbered .sql scripts. Relative paths resolve against the app's content root.
    public string ScriptsPath { get; set; } = "../../scripts";

    // Hard limits so a stuck connection or a blocked ALTER DATABASE shows up as an error card instead of a spinner.
    public int ActionTimeoutSeconds { get; set; } = 90;
    public int AdminTimeoutSeconds { get; set; } = 600;

    public string DemoConnectionString => WithApp(ConnectionString, DemoAppName);
    public string DiagnosticsConnectionString => WithApp(ConnectionString, DiagnosticsAppName);

    public string ServerHost => HostOf(ConnectionString);

    public static string HostOf(string cs)
    {
        var ds = new SqlConnectionStringBuilder(cs).DataSource ?? "";
        if (ds.StartsWith("tcp:", StringComparison.OrdinalIgnoreCase)) ds = ds[4..];
        return ds.Split(',')[0].Split('\\')[0].Trim();
    }

    public static bool IsLocal(string host) =>
        new[] { "localhost", "127.0.0.1", "::1", ".", "(local)", "host.docker.internal" }.Contains(host, StringComparer.OrdinalIgnoreCase);

    public bool CanRunAdminActions(out string reason)
    {
        if (!AllowAdminActions)
        {
            reason = "Admin actions are off. Set Lab:AllowAdminActions to true (it is on in appsettings.Development.json).";
            return false;
        }

        var host = ServerHost;
        if (IsLocal(host) || AdminHosts.Contains(host, StringComparer.OrdinalIgnoreCase))
        {
            reason = "";
            return true;
        }

        reason = $"Admin actions only run against a local server. '{host}' is not local; add it to Lab:AdminHosts if it is your own container.";
        return false;
    }

    private static string WithApp(string cs, string app)
    {
        var b = new SqlConnectionStringBuilder(cs) { ApplicationName = app };
        var keys = cs.ToLowerInvariant();
        if (!keys.Contains("connect timeout") && !keys.Contains("connection timeout")) b.ConnectTimeout = 10;
        if (!keys.Contains("command timeout")) b.CommandTimeout = 60;

        // SqlClient defaults to Encrypt=Mandatory. Against a local container that buys nothing, and the TLS handshake
        // is the step most likely to stall on macOS. Only applied when the connection string doesn't say.
        if (!keys.Contains("encrypt") && IsLocal(HostOf(cs))) b.Encrypt = SqlConnectionEncryptOption.Optional;
        return b.ConnectionString;
    }
}
