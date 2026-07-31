using GrainInterfaces;

namespace VPP_ORLEANS.Web;

public class ScheduleApiClient(HttpClient httpClient)
{
    public async Task<ScheduleResponse> GetAllAsync(CancellationToken ct = default)
        => await httpClient.GetFromJsonAsync<ScheduleResponse>("/schedule", ct) ?? new ScheduleResponse();

    public async Task<ScheduleResponse> AddAsync(string title, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/schedule", new { Title = title }, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScheduleResponse>(ct) ?? new ScheduleResponse();
    }

    public async Task<ScheduleResponse> ToggleAsync(string title, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsync($"/schedule/{Uri.EscapeDataString(title)}/toggle", null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ScheduleResponse>(ct) ?? new ScheduleResponse();
    }
}