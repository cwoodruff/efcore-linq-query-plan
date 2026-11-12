namespace LinqToQueryPlan.Lab.Demos;

public static class DemoCatalog
{
    public static IReadOnlyList<DemoInfo> All { get; } =
    [
        Demo01TheCase.Create(),
        Demo02ColdOpen.Create(),
        Demo03TagsAndSqlText.Create(),
        Demo04OptionalParameter.Create(),
        Demo05ContainsThreeWays.Create(),
        Demo06SplitVsSingle.Create(),
        Demo07Redaction.Create(),
        Demo08InterceptorHint.Create(),
        Demo09QueryStoreHint.Create(),
    ];

    public static DemoInfo? Find(string slugOrNumber) =>
        All.FirstOrDefault(d => d.Slug.Equals(slugOrNumber, StringComparison.OrdinalIgnoreCase) || d.Number.ToString() == slugOrNumber);
}
