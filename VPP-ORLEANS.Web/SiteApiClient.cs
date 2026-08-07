using VPP_ORLEANS.Contracts;
using VPP_ORLEANS.GrainInterfaces;

namespace VPP_ORLEANS.Web;

public class SiteApiClient(HttpClient httpClient)
{
    public async Task<SiteResponse> GetSitesAsync(int page, int pageSize, CancellationToken ct = default)
        => await httpClient.GetFromJsonAsync<SiteResponse>($"/site?page={page}&pageSize={pageSize}", ct)
            ?? new SiteResponse();

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

    public async Task DeleteSiteAsync(string title, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync($"/site/{Uri.EscapeDataString(title)}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveAssetAsync(string site, AssetKind kind, string assetId, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync(
            $"/site/{Uri.EscapeDataString(site)}/assets/{kind}/{Uri.EscapeDataString(assetId)}", ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task<AssetItem[]> GetAssetsAsync(string site, CancellationToken ct = default)
        => (await httpClient.GetFromJsonAsync<AssetListResponse>($"/site/{Uri.EscapeDataString(site)}/assets", ct)
                ?? new AssetListResponse()).Assets;

    public async Task<BatteryInfo> GetBatteryAsync(string assetId, CancellationToken ct = default)
        => await httpClient.GetFromJsonAsync<BatteryInfo>($"/assets/battery/{Uri.EscapeDataString(assetId)}", ct)
            ?? throw new InvalidOperationException("API returned an invalid response.");

    public async Task<AssetItem> AddAssetAsync(string site, AssetKind kind, string assetId, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/site/{Uri.EscapeDataString(site)}/assets",
            new { Kind = kind, AssetId = assetId },
            ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AssetItem>(ct)
            ?? throw new InvalidOperationException("API returned an invalid response.");
    }

    public async Task DispatchBatteryAsync(string assetId, double desiredKw, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync(
            $"/assets/battery/{Uri.EscapeDataString(assetId)}/dispatch",
            new { DesiredKw = desiredKw },
            ct);
        response.EnsureSuccessStatusCode();
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
