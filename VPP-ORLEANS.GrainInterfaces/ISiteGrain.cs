namespace VPP_ORLEANS.GrainInterfaces;

public interface ISiteGrain : IGrainWithStringKey
{
    Task Add();
    Task Toggle();
    Task<SiteState> Get();
}

public interface ISiteRegistryGrain : IGrainWithStringKey
{
    Task Register(string title);
    Task<string[]> GetAllTitles();
}

[GenerateSerializer]
public record SiteState
{
    [Id(0)] public string Title { get; init; } = "";
    [Id(1)] public bool IsActive { get; init; }

    public SiteState Toggle() => this with { IsActive = !IsActive };
}