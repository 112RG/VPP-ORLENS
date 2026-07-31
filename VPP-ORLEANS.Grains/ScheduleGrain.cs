using GrainInterfaces;

namespace VPP_ORLEANS.Grains;

public class ScheduleGrain : Grain, IScheduleGrain
{
    private readonly IPersistentState<ScheduleState> _state;
    private IGrainTimer? _ticker;

    public ScheduleGrain([PersistentState("schedule", "AdoNet")] IPersistentState<ScheduleState> state)
    {
        _state = state;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _ticker = this.RegisterGrainTimer(
            static (state, ct) => state.Tick(ct),
            this,
            new GrainTimerCreationOptions
            {
                DueTime = TimeSpan.FromSeconds(10),
                Period = TimeSpan.FromSeconds(10),
                KeepAlive = true
            });

        return Task.CompletedTask;
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        _ticker?.Dispose();
        return base.OnDeactivateAsync(reason, cancellationToken);
    }

    public async Task AddTask(string title)
    {
        if (!_state.State.Items.Any(t => t.Title == title))
            _state.State.Items.Add(new ScheduleItem { Title = title, IsDone = false });
        await _state.WriteStateAsync();
    }

    public async Task ToggleTask(string title)
    {
        var task = _state.State.Items.FirstOrDefault(t => t.Title == title);
        if (task is not null)
            task.IsDone = !task.IsDone;
        await _state.WriteStateAsync();
    }

    public Task<ScheduleItem[]> GetAllTasks() =>
        Task.FromResult(_state.State.Items.ToArray());

    public Task<DateTimeOffset> GetLastTick()
        => Task.FromResult(new DateTimeOffset(_state.State.LastTick, TimeSpan.Zero));

    private async Task Tick(CancellationToken ct)
    {
        _state.State.LastTick = DateTime.UtcNow;
        await _state.WriteStateAsync();
    }
}