using LinqToQueryPlan.Lab;
using LinqToQueryPlan.Lab.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LinqToQueryPlan.Web.Pages;

public class LabModel(LabOptions options, ServerDiagnostics diagnostics, LabAdmin admin, ConnectionDoctor doctor) : PageModel
{
    public bool AdminAllowed { get; private set; }
    public string AdminReason { get; private set; } = "";

    public void OnGet()
    {
        AdminAllowed = options.CanRunAdminActions(out var reason);
        AdminReason = reason;
    }

    public async Task<IActionResult> OnGetEnvironmentAsync() =>
        Partial("_Environment", await diagnostics.EnvironmentAsync());

    public async Task<IActionResult> OnGetPillAsync() =>
        Partial("_EnvPill", await diagnostics.EnvironmentAsync());

    public async Task<IActionResult> OnPostDoctorAsync() =>
        Partial("_Doctor", await doctor.RunAsync());

    public Task<IActionResult> OnPostSetupAsync() => Run(admin.SetupAsync);
    public Task<IActionResult> OnPostResetAsync() => Run(admin.ResetAsync);
    public Task<IActionResult> OnPostInflateAsync() => Run(admin.InflateAsync);

    private async Task<IActionResult> Run(Func<Task<ScriptResult>> script)
    {
        try
        {
            var result = await script();
            Response.Headers["HX-Trigger"] = "lab-changed";
            return Partial("_ScriptResult", result);
        }
        catch (Exception ex)
        {
            return Content($"<div class=\"card result\"><div class=\"body\"><div class=\"error\">{System.Net.WebUtility.HtmlEncode(ex.GetBaseException().Message)}</div></div></div>", "text/html");
        }
    }
}
