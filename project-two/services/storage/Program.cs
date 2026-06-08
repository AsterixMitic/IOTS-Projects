var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHealthChecks();

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "storage",
    status = "running",
    brokerMode = app.Configuration["BROKER_MODE"] ?? "mqtt"
}));

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/config", () => Results.Ok(new
{
    brokerMode = app.Configuration["BROKER_MODE"] ?? "mqtt",
    databaseUrl = app.Configuration["DATABASE_URL"] ?? "",
    connectionString = app.Configuration.GetConnectionString("Postgres") ?? ""
}));

app.Run();
