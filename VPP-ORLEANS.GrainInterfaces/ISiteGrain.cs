namespace VPP_ORLEANS.GrainInterfaces;

public interface ISiteGrain : IGrainWithStringKey
{
    Task Add();
    Task Toggle();
    Task<SiteState> Get();
}

public interface ISiteRegistryGrain : IGrainWithIntegerKey
{
    Task Register(string title);
    Task<SiteTitlePage> GetTitles(int skip, int take);
}

[GenerateSerializer]
public record SiteTitlePage
{
    [Id(0)] public string[] Titles { get; init; } = [];
    [Id(1)] public int Total { get; init; }
}

[GenerateSerializer]
public record SiteState
{
    [Id(0)] public string Title { get; init; } = "";
    [Id(1)] public bool IsActive { get; init; }

    public SiteState Toggle() => this with { IsActive = !IsActive };
}