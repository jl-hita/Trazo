using Microsoft.EntityFrameworkCore;
using Trazo.Application;
using Trazo.Infrastructure;
using Trazo.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:4200"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminFrontend", policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

// Apply all pending EF Core migrations before accepting HTTP traffic.
// This makes first deployment and subsequent upgrades use the same safe path.
await app.Services.InitializeDatabaseAsync();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
app.UseCors("AdminFrontend");

app.MapGet("/health", () => Results.Ok(new
{
    status = "healthy",
    service = "trazo-api"
}))
.WithName("Health");

app.MapGet("/health/ready", async (TrazoDbContext db, CancellationToken cancellationToken) =>
{
    var canConnect = await db.Database.CanConnectAsync(cancellationToken);
    return canConnect
        ? Results.Ok(new { status = "ready", database = "available" })
        : Results.Problem("Database is not available.", statusCode: StatusCodes.Status503ServiceUnavailable);
})
.WithName("Readiness");

app.MapGet("/api/v1", () => Results.Ok(new
{
    name = "TRAZO API",
    version = "v1"
}))
.WithName("ApiInfo");

app.Run();

public partial class Program
{
}
