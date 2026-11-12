using LinqToQueryPlan.Lab;
using LinqToQueryPlan.Lab.Diagnostics;
using LinqToQueryPlan.Web.Services;

var builder = WebApplication.CreateBuilder(args);

// Connection string: Lab:ConnectionString in appsettings, overridden by user secrets or the LAHMAN_CONNECTION
// environment variable (the same variable the console app reads). The application name is set in code.
var options = builder.Configuration.GetSection(LabOptions.Section).Get<LabOptions>() ?? new LabOptions();
if (Environment.GetEnvironmentVariable("LAHMAN_CONNECTION") is { Length: > 0 } fromEnv)
{
    options.ConnectionString = fromEnv;
}

var scripts = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, options.ScriptsPath));

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<ServerDiagnostics>();
builder.Services.AddSingleton<ConnectionDoctor>();
builder.Services.AddSingleton(sp => new LabAdmin(sp.GetRequiredService<LabOptions>(), scripts));
builder.Services.AddSingleton<NotesService>();
builder.Services.AddSingleton<PlanStore>();
builder.Services.AddMemoryCache();
builder.Services.AddRazorPages();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAntiforgery();
app.MapRazorPages();

// Download a captured plan to open in VS Code (MSSQL extension) or SSMS.
app.MapGet("/plans/{id}.sqlplan", (string id, PlanStore store) =>
    store.Get(id) is { } xml
        ? Results.File(System.Text.Encoding.UTF8.GetBytes(xml), "application/xml", $"linq-to-query-plan-{id[..8]}.sqlplan")
        : Results.NotFound("That plan has expired. Run the demo again."));

app.Run();
