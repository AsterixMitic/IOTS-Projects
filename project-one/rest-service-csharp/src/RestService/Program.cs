using Microsoft.EntityFrameworkCore;
using RestService.Application.Services;
using RestService.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<IotDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Postgres")));
builder.Services.AddScoped<IReadingsService, ReadingsService>();

var app = builder.Build();

app.MapOpenApi();
app.MapControllers();

app.Run();
