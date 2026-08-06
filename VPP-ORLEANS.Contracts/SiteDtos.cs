namespace VPP_ORLEANS.Contracts;

public record AddSiteRequest(string Title);

public record SiteItem
{
    public string Title { get; init; } = "";
    public bool IsActive { get; init; }
}

public record SiteResponse
{
    public SiteItem[] Sites { get; init; } = [];
    public int Total { get; init; }
    public int Page { get; init; }
    public int PageSize { get; init; }
}