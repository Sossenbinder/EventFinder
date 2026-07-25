using System.Globalization;
using System.Text.Json.Serialization;
using EventFinder.Api;
using EventFinder.Api.Cli;
using EventFinder.Api.Config;
using EventFinder.Api.Endpoints;
using EventFinder.Data;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.EntityFrameworkCore;
using Serilog;

// CLI verbs on the same executable (outline): `sources verify` and
// `ingest once`. No verb -> run the web host normally.
if (args is [var verb, var subVerb, ..]
    && string.Equals(verb, "sources", StringComparison.OrdinalIgnoreCase)
    && string.Equals(subVerb, "verify", StringComparison.OrdinalIgnoreCase))
{
    return await CliCommands.RunSourcesVerifyAsync(CancellationToken.None);
}
if (args is [var ingestVerb, var onceVerb, ..]
    && string.Equals(ingestVerb, "ingest", StringComparison.OrdinalIgnoreCase)
    && string.Equals(onceVerb, "once", StringComparison.OrdinalIgnoreCase))
{
    return await CliCommands.RunIngestOnceAsync(CancellationToken.None);
}

var builder = WebApplication.CreateBuilder(args);

builder.Configuration.AddEnvironmentVariables(prefix: "EVENTFINDER__");

builder.Host.UseSerilog((ctx, _, cfg) => cfg
    .ReadFrom.Configuration(ctx.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddEventFinder(builder.Configuration);

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.Providers.Add<BrotliCompressionProvider>();
    options.Providers.Add<GzipCompressionProvider>();
});

var corsOrigins = builder.Configuration.GetSection(CorsOptions.Section).Get<CorsOptions>()?.AllowedOrigins
    ?? new CorsOptions().AllowedOrigins;
builder.Services.AddCors(options => options.AddPolicy("frontend", policy => policy
    .WithOrigins(corsOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var ingestionEnabled = builder.Configuration.GetValue("Ingestion:Enabled", true);
if (ingestionEnabled)
{
    builder.Services.AddHostedService<IngestionBackgroundService>();
}

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<EventFinderDbContext>();
    await db.Database.MigrateAsync();
}

app.UseSerilogRequestLogging();
app.UseResponseCompression();
app.UseCors("frontend");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapEventsEndpoints();
app.MapIcsEndpoints();
app.MapPlacesEndpoints();
app.MapSourcesEndpoints();

// Serves web/'s Vite build (workstream 4). wwwroot is gitignored and may not
// exist locally; ASP.NET Core falls back to a NullFileProvider for a missing
// web root, so static/SPA-fallback middleware below is a no-op rather than a
// startup failure until the frontend is actually built and copied in.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "public, max-age=3600",
});
app.MapFallbackToFile("index.html");

app.Run();

return 0;

public partial class Program;
