using GrainInterfaces;
using Orleans.Configuration;
using Orleans.Hosting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

builder.UseOrleansClient(client =>
{
    var connectionString = builder.Configuration.GetConnectionString("orleans-db")
        ?? throw new InvalidOperationException("Connection string 'orleans-db' not found");

    client.UseAdoNetClustering(options =>
    {
        options.Invariant = "Npgsql";
        options.ConnectionString = connectionString;
    });
});

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapGet("/", () => "API service is running.");

app.MapGet("/schedule", async (IClusterClient cluster) =>
{
    var grain = cluster.GetGrain<IScheduleGrain>("default");
    var tasks = await grain.GetAllTasks();
    var lastTick = await grain.GetLastTick();
    return new ScheduleResponse { Tasks = tasks, LastTick = lastTick };
});

app.MapPost("/schedule", async (AddTaskRequest req, IClusterClient cluster) =>
{
    var grain = cluster.GetGrain<IScheduleGrain>("default");
    await grain.AddTask(req.Title);
    var tasks = await grain.GetAllTasks();
    var lastTick = await grain.GetLastTick();
    return new ScheduleResponse { Tasks = tasks, LastTick = lastTick };
});

app.MapPut("/schedule/{title}/toggle", async (string title, IClusterClient cluster) =>
{
    var grain = cluster.GetGrain<IScheduleGrain>("default");
    await grain.ToggleTask(title);
    var tasks = await grain.GetAllTasks();
    var lastTick = await grain.GetLastTick();
    return new ScheduleResponse { Tasks = tasks, LastTick = lastTick };
});

app.MapDefaultEndpoints();

app.Run();

record AddTaskRequest(string Title);