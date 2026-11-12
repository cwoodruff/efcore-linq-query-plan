using System.Globalization;
using System.Text.Json;
using LinqToQueryPlan.Lab.Model;

namespace LinqToQueryPlan.Web.Services;

public static class Ui
{
    public static string Rows(double v) => v >= 100 ? v.ToString("N0", CultureInfo.InvariantCulture) : v.ToString("0.#", CultureInfo.InvariantCulture);

    public static string Pct(double v) => v.ToString("0", CultureInfo.InvariantCulture) + "%";

    // Log-scaled bar width (0-100) so 1 row and 4,000,000 rows both fit on one screen.
    public static string LogWidth(double value, double max)
    {
        if (value <= 0 || max <= 0) return "0";
        var w = 100 * Math.Log10(value + 1) / Math.Log10(max + 1);
        return Math.Clamp(w, 2, 100).ToString("0.#", CultureInfo.InvariantCulture);
    }

    public static string Width(double pct) => Math.Clamp(pct, 0, 100).ToString("0.#", CultureInfo.InvariantCulture);

    public static string KindClass(ObservationKind k) => k switch
    {
        ObservationKind.Good => "obs-good",
        ObservationKind.Warn => "obs-warn",
        _ => "obs-info",
    };

    // Chart.js config. Colors are applied in site.js from CSS variables so charts follow the theme.
    public static string ChartJson(ChartSpec c) => JsonSerializer.Serialize(new
    {
        type = c.Type,
        data = new
        {
            labels = c.Labels,
            datasets = c.Series.Select(s => new { label = s.Label, data = s.Data }),
        },
        options = new
        {
            indexAxis = c.Horizontal ? "y" : "x",
            responsive = true,
            maintainAspectRatio = false,
            plugins = new
            {
                title = new { display = true, text = c.Title },
                legend = new { display = c.Series.Count > 1 },
            },
            scales = new
            {
                y = new
                {
                    type = c.LogScale ? "logarithmic" : "linear",
                    title = new { display = c.YTitle is not null, text = c.YTitle ?? "" },
                },
            },
        },
    });
}
