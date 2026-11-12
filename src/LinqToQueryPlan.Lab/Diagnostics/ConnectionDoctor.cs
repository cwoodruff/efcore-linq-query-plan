using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Microsoft.Data.SqlClient;

namespace LinqToQueryPlan.Lab.Diagnostics;

// Walks the connection one layer at a time so a hang has a name: name lookup, TCP, TLS + login, database, permissions.
// Every step has its own short limit, so the check itself can't hang.
public sealed class ConnectionDoctor(LabOptions options)
{
    public async Task<DoctorReport> RunAsync()
    {
        var report = new DoctorReport();
        SqlConnectionStringBuilder configured;
        try
        {
            configured = new SqlConnectionStringBuilder(options.DemoConnectionString);
        }
        catch (Exception ex)
        {
            report.Add("Parse connection string", false, 0, ex.Message);
            report.Advice.Add("The connection string doesn't parse. If you set LAHMAN_CONNECTION in zsh with double quotes, '!' characters in the password get expanded by the shell. Use single quotes.");
            return report;
        }

        var raw = new SqlConnectionStringBuilder(options.ConnectionString);
        var host = LabOptions.HostOf(options.ConnectionString);
        var port = PortOf(configured.DataSource);
        report.Add("Connection string", true, 0,
            $"server {configured.DataSource}, database {configured.InitialCatalog}, user {configured.UserID}, " +
            $"password {configured.Password.Length} characters, Encrypt={configured.Encrypt}, TrustServerCertificate={configured.TrustServerCertificate}, " +
            $"Connect Timeout={configured.ConnectTimeout}s, source {(Environment.GetEnvironmentVariable("LAHMAN_CONNECTION") is { Length: > 0 } ? "LAHMAN_CONNECTION" : "appsettings")}");
        if (raw.Password == "CHANGE_ME")
            report.Advice.Add("The password is still CHANGE_ME. Set Lab:ConnectionString in appsettings.Development.json or the LAHMAN_CONNECTION environment variable.");

        // 1. Name lookup
        IPAddress[] addresses = [];
        var sw = Stopwatch.StartNew();
        try
        {
            addresses = IPAddress.TryParse(host, out var ip) ? [ip] : await WithLimit(Dns.GetHostAddressesAsync(host), 5);
            report.Add("Resolve host name", true, sw.ElapsedMilliseconds, $"{host} -> {string.Join(", ", addresses.Select(a => a.ToString()))}");
        }
        catch (Exception ex)
        {
            report.Add("Resolve host name", false, sw.ElapsedMilliseconds, ex.Message);
            return report;
        }

        // 2. TCP to each address
        var reachable = new List<IPAddress>();
        foreach (var a in addresses)
        {
            sw.Restart();
            try
            {
                using var tcp = new TcpClient(a.AddressFamily);
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                await tcp.ConnectAsync(a, port, cts.Token);
                reachable.Add(a);
                report.Add($"TCP connect {a}:{port}", true, sw.ElapsedMilliseconds, "port is open");
            }
            catch (Exception ex)
            {
                report.Add($"TCP connect {a}:{port}", false, sw.ElapsedMilliseconds, ex is OperationCanceledException ? "no answer in 3s" : ex.Message);
            }
        }
        if (reachable.Count == 0)
        {
            report.Advice.Add($"Nothing is listening on {host}:{port}. Check that the container is running (docker ps) and publishes port {port}.");
            return report;
        }
        if (reachable.Count < addresses.Length && host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            report.Advice.Add($"'localhost' resolves to an address that doesn't answer. Use Server=127.0.0.1,{port} in the connection string.");

        // 3. SQL login, as configured and with variations, each with a hard limit
        var variants = new List<(string Name, SqlConnectionStringBuilder B)> { ("SQL login (as configured)", Copy(configured, b => { })) };
        if (configured.Encrypt != SqlConnectionEncryptOption.Optional)
            variants.Add(("SQL login with Encrypt=False", Copy(configured, b => b.Encrypt = SqlConnectionEncryptOption.Optional)));
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            variants.Add(("SQL login via 127.0.0.1", Copy(configured, b => b.DataSource = $"127.0.0.1,{port}")));

        string? working = null;
        foreach (var (name, b) in variants)
        {
            b.ConnectTimeout = 8;
            b.Pooling = false;
            sw.Restart();
            try
            {
                await using var c = new SqlConnection(b.ConnectionString);
                await WithLimit(c.OpenAsync(), 12);
                await using var cmd = new SqlCommand("SELECT CAST(SERVERPROPERTY('ProductVersion') AS nvarchar(40)), DB_NAME(), (SELECT compatibility_level FROM sys.databases WHERE name = DB_NAME()), HAS_PERMS_BY_NAME(NULL, NULL, 'VIEW SERVER PERFORMANCE STATE');", c) { CommandTimeout = 10 };
                await using var r = await WithLimit(cmd.ExecuteReaderAsync(), 12);
                await r.ReadAsync();
                report.Add(name, true, sw.ElapsedMilliseconds,
                    $"SQL Server {r.GetString(0)}, database {r.GetString(1)}, compat {r.GetByte(2)}, VIEW SERVER PERFORMANCE STATE {(r.GetInt32(3) == 1 ? "yes" : "NO")}");
                working ??= name;
                if (r.GetInt32(3) != 1)
                    report.Advice.Add("This login can't read the plan cache DMVs. Connect as sa (local container) or GRANT VIEW SERVER PERFORMANCE STATE.");
            }
            catch (TimeoutException)
            {
                report.Add(name, false, sw.ElapsedMilliseconds, "no answer in 12s (the connection stalled; this is the hang)");
            }
            catch (Exception ex)
            {
                var msg = ex.GetBaseException().Message;
                if (msg.Contains("error 258", StringComparison.OrdinalIgnoreCase))
                    msg = "the port accepted the connection but SQL Server never finished the login handshake (timeout, SqlClient error 258)";
                report.Add(name, false, sw.ElapsedMilliseconds, msg);
            }
        }

        if (working is null)
        {
            report.Advice.Add("TCP works but no SQL login completed. If the failures say 'Login failed', the user or password is wrong. If they say the handshake never finished, something on that port isn't answering as SQL Server: check docker ps, docker logs on the container, and that the port mapping points at the SQL Server container. If only the Encrypt=False row works, add Encrypt=False to the connection string.");
        }
        else if (working != variants[0].Name)
        {
            report.Advice.Add($"The configured connection doesn't work, but '{working}' does. Change the connection string to match.");
        }
        else
        {
            report.Advice.Add("The connection works. If demos still stall, the error card now says which step was waiting.");
        }
        return report;
    }

    private static int PortOf(string dataSource)
    {
        var parts = dataSource.Split(',');
        return parts.Length > 1 && int.TryParse(parts[1].Trim(), out var p) ? p : 1433;
    }

    private static SqlConnectionStringBuilder Copy(SqlConnectionStringBuilder source, Action<SqlConnectionStringBuilder> change)
    {
        var b = new SqlConnectionStringBuilder(source.ConnectionString);
        change(b);
        return b;
    }

    private static async Task<T> WithLimit<T>(Task<T> task, int seconds)
    {
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))) != task)
        {
            _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            throw new TimeoutException();
        }
        return await task;
    }

    private static async Task WithLimit(Task task, int seconds)
    {
        if (await Task.WhenAny(task, Task.Delay(TimeSpan.FromSeconds(seconds))) != task)
        {
            _ = task.ContinueWith(t => _ = t.Exception, TaskScheduler.Default);
            throw new TimeoutException();
        }
        await task;
    }
}

public sealed class DoctorReport
{
    public List<DoctorStep> Steps { get; } = [];
    public List<string> Advice { get; } = [];
    public bool AllOk => Steps.All(s => s.Ok);
    public void Add(string name, bool ok, long ms, string detail) => Steps.Add(new(name, ok, ms, detail));
}

public sealed record DoctorStep(string Name, bool Ok, long Ms, string Detail);
