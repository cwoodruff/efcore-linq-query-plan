using LinqToQueryPlan.Lab.Model;
using Microsoft.Extensions.Caching.Memory;

namespace LinqToQueryPlan.Web.Services;

// Holds captured plan XML for an hour so the "Download .sqlplan" link works.
public sealed class PlanStore(IMemoryCache cache)
{
    public void Add(RunResult result)
    {
        foreach (var p in result.Queries.SelectMany(q => q.Plans))
        {
            cache.Set("plan:" + p.Id, p.PlanXml, TimeSpan.FromHours(1));
        }
    }

    public string? Get(string id) => cache.TryGetValue("plan:" + id, out string? xml) ? xml : null;
}
