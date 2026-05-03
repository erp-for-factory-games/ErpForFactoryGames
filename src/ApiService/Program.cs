using ERP.Application.Queries.PlanProduction;
using ERP.Domain;
using ERP.Infrastructure;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Host.UseWolverine(opts =>
{
    opts.Discovery.IncludeAssembly(typeof(ERP.Application.IRecipeCatalog).Assembly);
});

builder.Services.AddErpInfrastructure();
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/", () => "API service is running. See /catalog/items and /plan.");

string[] summaries = ["Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"];

app.MapGet("/weatherforecast", () =>
    Enumerable.Range(1, 5).Select(index => new WeatherForecast(
        DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
        Random.Shared.Next(-20, 55),
        summaries[Random.Shared.Next(summaries.Length)]))
    .ToArray())
.WithName("GetWeatherForecast");

app.MapGet("/catalog/items", (ERP.Application.IRecipeCatalog catalog) =>
    catalog.Items.Select(i => new ItemDto(i.Id.Value, i.Name)));

app.MapPost("/plan", async (PlanRequest request, IMessageBus bus) =>
{
    var query = new PlanProductionQuery(
        Targets:   request.Targets.Select(t => new ProductionTarget(new ItemId(t.ItemId), t.ItemsPerMinute)).ToList(),
        Available: request.Available.Select(a => new ResourceAvailability(new ItemId(a.ItemId), a.ItemsPerMinute)).ToList());

    var plan = await bus.InvokeAsync<ProductionPlan>(query);
    return PlanDto.From(plan);
});

app.MapDefaultEndpoints();

app.Run();

public record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

public sealed record ItemDto(string Id, string Name);

public sealed record TargetDto(string ItemId, decimal ItemsPerMinute);
public sealed record AvailabilityDto(string ItemId, decimal ItemsPerMinute);
public sealed record PlanRequest(IReadOnlyList<TargetDto> Targets, IReadOnlyList<AvailabilityDto> Available);

public sealed record AmountDto(string ItemId, string ItemName, decimal ItemsPerMinute);
public sealed record StepDto(
    string RecipeId,
    string RecipeName,
    string BuildingId,
    decimal BuildingCount,
    IReadOnlyList<AmountDto> Inputs,
    IReadOnlyList<AmountDto> Outputs);

public sealed record PlanDto(
    bool IsFeasible,
    IReadOnlyList<StepDto> Steps,
    IReadOnlyList<AmountDto> RawInputsConsumed,
    IReadOnlyList<AmountDto> MissingInputs)
{
    public static PlanDto From(ProductionPlan plan) => new(
        IsFeasible: plan.IsFeasible,
        Steps: plan.Steps.Select(s => new StepDto(
            s.Recipe.Id.Value,
            s.Recipe.Name,
            s.Recipe.Building.Value,
            Math.Round(s.BuildingCount, 4),
            s.InputsPerMinute.Select(ToAmount).ToList(),
            s.OutputsPerMinute.Select(ToAmount).ToList())).ToList(),
        RawInputsConsumed: plan.RawInputsConsumed.Select(ToAmount).ToList(),
        MissingInputs:    plan.MissingInputs.Select(ToAmount).ToList());

    private static AmountDto ToAmount(ItemAmount a) =>
        new(a.Item.Value, Satisfactory.Catalog.SatisfactoryCatalog.FindItem(a.Item)?.Name ?? a.Item.Value, Math.Round(a.Quantity, 4));
}
