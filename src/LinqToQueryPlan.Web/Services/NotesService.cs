using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using LinqToQueryPlan.Lab.Demos;
using Markdig;

namespace LinqToQueryPlan.Web.Services;

// Renders the Markdown notes in Notes/. The same files read fine on GitHub; links between them
// (03-sniffing.md, getting-started.md) are rewritten to app routes here.
public sealed partial class NotesService(IWebHostEnvironment env)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();
    private readonly ConcurrentDictionary<string, string> _cache = new();

    public string Render(string file)
    {
        if (!env.IsDevelopment() && _cache.TryGetValue(file, out var cached)) return cached;

        var path = Path.Combine(env.ContentRootPath, "Notes", file);
        if (!File.Exists(path)) return $"<p class=\"muted\">Notes file <code>{file}</code> not found.</p>";

        var html = Markdown.ToHtml(File.ReadAllText(path), Pipeline);
        html = NoteLink().Replace(html, m =>
        {
            var target = m.Groups[1].Value;
            if (target == "getting-started.md") return "href=\"/getting-started\"";
            var demo = DemoCatalog.All.FirstOrDefault(d => d.NotesFile == target);
            return demo is null ? m.Value : $"href=\"/demos/{demo.Slug}\"";
        });
        html = html.Replace("<a href=\"http", "<a target=\"_blank\" rel=\"noopener\" href=\"http");

        _cache[file] = html;
        return html;
    }

    [GeneratedRegex("href=\"([\\w-]+\\.md)\"")]
    private static partial Regex NoteLink();
}
