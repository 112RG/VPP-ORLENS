using VPP_ORLEANS.Contracts;

namespace VPP_ORLEANS.Web;

public class SiteApiClient(HttpClient httpClient)
{
    public async Task<SiteResponse> GetAllAsync(CancellationToken ct = default)
        => await httpClient.GetFromJsonAsync<SiteResponse>("/site", ct) ?? new SiteResponse();

    public async Task<SiteItem> AddAsync(string title, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/site", new { Title = title }, ct);
        response.EnsureSuccessStatusCode();
        return SingleSite(await ReadSiteResponseAsync(response, ct));
    }

    public async Task<SiteItem> ToggleAsync(string title, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsync($"/site/{Uri.EscapeDataString(title)}/toggle", null, ct);
        response.EnsureSuccessStatusCode();
        return SingleSite(await ReadSiteResponseAsync(response, ct));
    }

    private static async Task<SiteResponse> ReadSiteResponseAsync(HttpResponseMessage response, CancellationToken ct)
        => await response.Content.ReadFromJsonAsync<SiteResponse>(ct)
            ?? throw new InvalidOperationException("API returned an invalid response.");

    private static SiteItem SingleSite(SiteResponse body)
        => body.Sites switch
        {
            [{ } item] => item,
            _ => throw new InvalidOperationException("API returned no site in the response.")
        };
}
