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
        var body = await response.Content.ReadFromJsonAsync<SiteResponse>(ct)
            ?? new SiteResponse();
        return body.Sites is [{ } item] ? item : new SiteItem { Title = title, IsActive = true };
    }

    public async Task<SiteItem> ToggleAsync(string title, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsync($"/site/{Uri.EscapeDataString(title)}/toggle", null, ct);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SiteResponse>(ct)
            ?? new SiteResponse();
        return body.Sites is [{ } item] ? item : new SiteItem { Title = title };
    }
}