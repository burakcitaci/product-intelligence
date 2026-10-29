using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using ProductHub.Api.Middleware;
using ProductHub.Api.Telemetry;
using ProductHub.Application;
using ProductHub.Infrastructure;
using ProductHub.Infrastructure.Persistence;

LoadLocalConnectionString();
var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddProductHubOpenTelemetry(builder.Configuration, builder.Environment);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddCors(options =>
    options.AddPolicy(
        "local-web",
        policy =>
            policy
                .WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()
    )
);
builder
    .Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "ProductHub.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

if (
    !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"])
    && !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_HEADERS"])
)
{
    app.Logger.LogInformation(
        "OpenTelemetry log export is enabled for service {ServiceName}",
        builder.Configuration["OTEL_SERVICE_NAME"] ?? "product-hub-api"
    );
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await SampleDataSeeder.SeedAsync(app.Services);
}

app.UseCors("local-web");

app.UseExceptionHandler();
app.UseAuthentication();
if (app.Environment.IsEnvironment("Testing"))
{
    app.Use(
        async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated != true)
                context.User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        new[]
                        {
                            new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()),
                            new Claim(ClaimTypes.Name, "Integration Test Admin"),
                            new Claim(ClaimTypes.Email, "test@producthub.local"),
                            new Claim(ClaimTypes.Role, "Admin"),
                        },
                        "IntegrationTests"
                    )
                );
            await next();
        }
    );
}
app.UseAuthorization();
app.MapControllers();

app.Run();

static void LoadLocalConnectionString()
{
    if (
        !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable("ConnectionStrings__ProductHub")
        )
    )
        return;

    var directory = new DirectoryInfo(Directory.GetCurrentDirectory());
    while (directory is not null)
    {
        var envPath = Path.Combine(directory.FullName, ".env");
        if (File.Exists(envPath))
        {
            var passwordLine = File.ReadLines(envPath)
                .FirstOrDefault(line =>
                    line.TrimStart().StartsWith("MSSQL_SA_PASSWORD=", StringComparison.Ordinal)
                );
            var password = passwordLine?.Split('=', 2)[1].Trim();
            if (!string.IsNullOrWhiteSpace(password))
                Environment.SetEnvironmentVariable(
                    "ConnectionStrings__ProductHub",
                    $"Server=127.0.0.1,1433;Database=ProductHub;User Id=sa;Password={password};TrustServerCertificate=True;Encrypt=True;Connect Timeout=15"
                );
            return;
        }
        directory = directory.Parent;
    }
}

public partial class Program;
