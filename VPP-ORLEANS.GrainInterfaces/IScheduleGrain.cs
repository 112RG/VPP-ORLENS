using Orleans;

namespace GrainInterfaces;

public interface IScheduleGrain : IGrainWithStringKey
{
    Task AddTask(string title);
    Task ToggleTask(string title);
    Task<ScheduleItem[]> GetAllTasks();
    Task<DateTimeOffset> GetLastTick();
}

[GenerateSerializer]
public record ScheduleState
{
    [Id(0)] public List<ScheduleItem> Items { get; set; } = [];
    [Id(2)] public DateTime LastTick { get; set; } = DateTime.UtcNow;
}

[GenerateSerializer]
public record ScheduleResponse
{
    [Id(0)] public ScheduleItem[] Tasks { get; set; } = [];
    [Id(1)] public DateTimeOffset LastTick { get; set; }
}

[GenerateSerializer]
public record ScheduleItem
{
    [Id(0)] public string Title { get; set; } = "";
    [Id(1)] public bool IsDone { get; set; }
}