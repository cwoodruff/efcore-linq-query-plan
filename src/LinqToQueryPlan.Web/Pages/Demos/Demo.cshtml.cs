using LinqToQueryPlan.Lab;
using LinqToQueryPlan.Lab.Demos;
using LinqToQueryPlan.Lab.Diagnostics;
using LinqToQueryPlan.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LinqToQueryPlan.Web.Pages.Demos;

public class DemoModel(LabOptions options, ServerDiagnostics diagnostics, LabAdmin admin, NotesService notes, PlanStore plans) : PageModel
{
    public DemoInfo Demo { get; private set; } = null!;
    public string NotesHtml { get; private set; } = "";
    public bool AdminAllowed { get; private set; }
    public string AdminReason { get; private set; } = "";
    public DemoInfo? Previous { get; private set; }
    public DemoInfo? Next { get; private set; }

    public IActionResult OnGet(string slug)
    {
        if (!Load(slug)) return NotFound();
        NotesHtml = notes.Render(Demo.NotesFile);
        var i = DemoCatalog.All.ToList().IndexOf(Demo);
        Previous = i > 0 ? DemoCatalog.All[i - 1] : null;
        Next = i < DemoCatalog.All.Count - 1 ? DemoCatalog.All[i + 1] : null;
        return Page();
    }

    // POST /demos/{slug}?handler=Run  (htmx). Returns one result card that htmx prepends to #results.
    public async Task<IActionResult> OnPostRunAsync(string slug, string action)
    {
        if (!Load(slug)) return NotFound();
        var a = Demo.Actions.FirstOrDefault(x => x.Key == action);
        if (a is null) return BadRequest($"Unknown action {action}");

        var values = Request.Form.Keys.Where(k => k != "action" && k != "__RequestVerificationToken")
            .ToDictionary(k => k, k => (string?)Request.Form[k].ToString());

        var result = await DemoRunner.RunAsync(Demo, a, values, options, diagnostics, admin);
        plans.Add(result);
        return Partial("_RunResult", result);
    }

    public Task<IActionResult> OnGetPlanCacheAsync(string slug) =>
        Panel(slug, async () => Partial("_PlanCache", await diagnostics.PlanCacheByTagAsync(Demo.TagPrefix)));

    public Task<IActionResult> OnGetQueryStoreAsync(string slug) =>
        Panel(slug, async () => Partial("_QueryStore", await diagnostics.QueryStoreAsync(Demo.TagPrefix)));

    public Task<IActionResult> OnGetXeAsync(string slug, string? tag) =>
        Panel(slug, async () => Partial("_XeEvents", await diagnostics.ReadXeAsync(tag ?? Demo.TagPrefix)));

    // Server views are read-only diagnostics; a failure shows as a card instead of a stack trace.
    private async Task<IActionResult> Panel(string slug, Func<Task<IActionResult>> render)
    {
        if (!Load(slug)) return NotFound();
        try
        {
            return await render();
        }
        catch (Exception ex)
        {
            var msg = System.Net.WebUtility.HtmlEncode(ex.GetBaseException().Message);
            return Content($"<div class=\"card\"><div class=\"result\"><div class=\"error\" style=\"padding:0.6rem 0.8rem\">Server view failed: {msg}</div></div></div>", "text/html");
        }
    }

    private bool Load(string slug)
    {
        if (DemoCatalog.Find(slug) is not { } d) return false;
        Demo = d;
        AdminAllowed = options.CanRunAdminActions(out var reason);
        AdminReason = reason;
        return true;
    }
}
