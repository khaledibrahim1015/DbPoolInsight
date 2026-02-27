using EFCore.Observability.Extensions;
using EFCore.Observability.otel.apiv2.Data;
using EFCore.Observability.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


builder.Services.AddEFCoreObservability(opts =>
{
    opts.TrackRentDurations = true;
    opts.EnableDiagnosticLogging = true;
    opts.MaxActivityHistoryPerContext = 500;
    opts.TrackStandardContexts = true;
});


builder.Services.AddDbContextPool<PrimaryDbContext>(
   (sp, options) =>
   {
       options.UseSqlServer(builder.Configuration.GetConnectionString("TMS_Conn"))
                .UseObservability<PrimaryDbContext>(sp, poolSize: 128);
   },
   poolSize: 128);

// TrackStandardContexts
builder.Services.AddDbContext<ReplicaDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("TMSReplica_Conn"));
});







builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
app.UseEFCoreObservability();
// or 
//app.Services.UseEFCoreObservability();





// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();


/// <summary>
/// GET /api/pooldiagnostics/metrics
/// Get metrics for all tracked contexts
/// </summary>
app.MapGet("/diagnostics/efcore/metrics", (DiagnosticsQueryService svc) =>
    svc.GetAllDetails());

/////////////////// Sequential test endpoint //////////////////////////



/// <summary>
/// POST /api/pooldiagnostics/test/sequential
/// Sequential load test - should maximize reuse
/// </summary>
app.MapPost("/api/pool/test/sequential",
    async (IServiceScopeFactory scopeFactory,
            ILogger<Program> logger,
            [FromQuery] string contextName = "PrimaryDbContext",
            [FromQuery] int requests = 10,
            [FromQuery] int delayMs = 50) =>
    {
        logger.LogInformation("Starting sequential load test: {Requests} requests", requests);

        // Run requests sequentially
        for (int i = 0; i < requests; i++)
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<PrimaryDbContext>();

            // Trigger actual DB query to ensure context is fully initialized
            await context.Bills.CountAsync();

            // Small delay between requests
            await Task.Delay(delayMs);
        }


        await Task.Delay(500);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(200);

        return Results.Ok();
    });

/// <summary>
/// POST /api/standard/test/sequential
/// Sequential load test - should maximize reuse
/// </summary>
app.MapPost("/api/standard/test/sequential",
    async (IServiceScopeFactory scopeFactory,
            ILogger<Program> logger,
            [FromQuery] string contextName = "ReplicaDbContext",
            [FromQuery] int requests = 10,
            [FromQuery] int delayMs = 50) =>
    {
        logger.LogInformation("Starting sequential load test: {Requests} requests", requests);

        // Run requests sequentially
        for (int i = 0; i < requests; i++)
        {
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<ReplicaDbContext>();

            // Trigger actual DB query to ensure context is fully initialized
            await context.Bills.CountAsync();

            // Small delay between requests
            await Task.Delay(delayMs);
        }


        await Task.Delay(500);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(200);

        return Results.Ok();
    });


/////////////////// concurent  test endpoint //////////////////////////
/// <summary>
/// POST /api/pool/test/concurrent
/// Concurrent load test - should show pool expansion
/// </summary>
app.MapPost("/api/pool/test/concurrent",
    async (IServiceScopeFactory scopeFactory,
            ILogger<Program> logger,
            [FromQuery] string contextName = "PrimaryDbContext",
            [FromQuery] int parallelRequests = 10,
            [FromQuery] int delayMs = 100) =>
    {
        logger.LogInformation("Starting concurrent load test: {Requests} parallel requests", parallelRequests);

        // Launch parallel requests
        var tasks = new List<Task>();
        for (int i = 0; i < parallelRequests; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<PrimaryDbContext>();

                // Trigger DB query
                await context.Bills.CountAsync();

                // Hold the lease to force other requests to get new instances
                await Task.Delay(delayMs);
            }));
        }

        await Task.WhenAll(tasks);

        // CRITICAL FIX: Wait for all async disposal/returns to complete
        await Task.Delay(500);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(200);

        return Results.Ok();
    });

/// <summary>
/// POST /api/standard/test/concurrent
/// Concurrent load test - should show pool expansion
/// </summary>
app.MapPost("/api/standard/test/concurrent",
    async (IServiceScopeFactory scopeFactory,
            ILogger<Program> logger,
            [FromQuery] string contextName = "ReplicaDbContext",
            [FromQuery] int parallelRequests = 10,
            [FromQuery] int delayMs = 100) =>
    {
        logger.LogInformation("Starting concurrent load test: {Requests} parallel requests", parallelRequests);

        // Launch parallel requests
        var tasks = new List<Task>();
        for (int i = 0; i < parallelRequests; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                using var scope = scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ReplicaDbContext>();

                // Trigger DB query
                await context.Bills.CountAsync();
                await Task.Delay(delayMs);
            }));
        }

        await Task.WhenAll(tasks);

        // CRITICAL FIX: Wait for all async disposal/returns to complete
        await Task.Delay(500);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Task.Delay(200);

        return Results.Ok();
    });

/////////////////// sustained-high-load //////////////////////////


/// <summary>
/// GET /api/pooldiagnostics/sustained-load
/// Sustained load test with waves of concurrent requests
/// </summary>
app.MapGet("/api/pooldiagnostics/sustained-load",
    async (IServiceScopeFactory scopeFactory,
            DiagnosticsQueryService svc,
            ILogger<Program> logger,
            [FromQuery] int waves = 5,
            [FromQuery] int requestsPerWave = 10,
            [FromQuery] int delayMs = 100) =>
    {
        logger.LogInformation("Starting sustained load: {Waves} waves × {Requests} requests", waves, requestsPerWave);

        for (int wave = 0; wave < waves; wave++)
        {
            logger.LogInformation("Starting wave {Wave}/{Total}", wave + 1, waves);

            var tasks = Enumerable.Range(0, requestsPerWave)
                .Select(async i =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<PrimaryDbContext>();
                    var count = await context.Bills.CountAsync();
                    logger.LogTrace("Wave {Wave}, Request {Index} completed", wave + 1, i + 1);
                });

            await Task.WhenAll(tasks);

            if (wave < waves - 1)
            {
                await Task.Delay(delayMs);
            }
        }

        // Wait for final contexts to be returned
        await Task.Delay(500);

        // Force garbage collection to ensure all contexts are cleaned up
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        await Task.Delay(100);

        return Results.Ok(new
        {
            Message = "Sustained load completed",
            TotalRequests = waves * requestsPerWave,
            Waves = waves,
            RequestsPerWave = requestsPerWave,
            Metrics = svc.GetPooledMetrics("PrimaryDbContext")
        });
    });

app.MapGet("/api/pooldiagnostics/sustained-high-load",
    async (IServiceScopeFactory scopeFactory,
            DiagnosticsQueryService svc,
            ILogger<Program> logger,
            [FromQuery] int waves = 5,
            [FromQuery] int requestsPerWave = 10,
            [FromQuery] int delayMs = 100) =>
    {
        logger.LogInformation("Starting sustained load: {Waves} waves × {Requests} requests", waves, requestsPerWave);

        for (int wave = 0; wave < waves; wave++)
        {
            logger.LogInformation("Starting wave {Wave}/{Total}", wave + 1, waves);

            var tasks = Enumerable.Range(0, requestsPerWave)
                .Select(async i =>
                {
                    using var scope = scopeFactory.CreateScope();
                    var context = scope.ServiceProvider.GetRequiredService<PrimaryDbContext>();
                    await Task.Delay(2000);  // Hold the context for 2 seconds to simulate long-running work and force pool expansion under load
                    var count = await context.Bills.CountAsync();
                    logger.LogTrace("Wave {Wave}, Request {Index} completed", wave + 1, i + 1);
                });

            await Task.WhenAll(tasks);

            if (wave < waves - 1)
            {
                await Task.Delay(delayMs);
            }
        }



        return Results.Ok(new
        {
            Message = "Sustained high load completed",
            TotalRequests = waves * requestsPerWave,
            Waves = waves,
            RequestsPerWave = requestsPerWave,
            Metrics = svc.GetPooledMetrics("PrimaryDbContext")
        });
    });



app.Run();
