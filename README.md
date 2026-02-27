# EFCore.Observability — Integration & Setup Guide

> **Version:** 1.0 · **Audience:** Developers integrating the library into ASP.NET Core applications

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Installation](#2-installation)
3. [Quick Start (5 minutes)](#3-quick-start-5-minutes)
4. [Full Registration Reference](#4-full-registration-reference)
5. [Configuring DbContext Types](#5-configuring-dbcontext-types)
   - [Pooled DbContext](#51-pooled-dbcontext)
   - [Standard (Non-Pooled) DbContext](#52-standard-non-pooled-dbcontext)
   - [Multiple DbContext Types](#53-multiple-dbcontext-types)
6. [ObservabilityOptions Reference](#6-observabilityoptions-reference)
7. [OpenTelemetry Integration](#7-opentelemetry-integration)
8. [HTTP Diagnostics API](#8-http-diagnostics-api)
9. [Reading Metrics Programmatically](#9-reading-metrics-programmatically)
10. [Prometheus & Grafana Setup](#10-prometheus--grafana-setup)
11. [Alerting Rules](#11-alerting-rules)
12. [Validating Your Installation](#12-validating-your-installation)
13. [Troubleshooting](#13-troubleshooting)

---

## 1. Prerequisites

| Requirement | Minimum Version |
|---|---|
| .NET | 6.0 |
| Entity Framework Core | 6.0 |
| ASP.NET Core | 6.0 |
| OpenTelemetry .NET (optional) | 1.5.0 |

---

## 2. Installation

```bash
# Core library
dotnet add package EFCore.Observability

# Optional: OpenTelemetry exporter support
dotnet add package EFCore.Observability.OpenTelemetry

# OpenTelemetry packages (if using OTel)
dotnet add package OpenTelemetry.Extensions.Hosting
dotnet add package OpenTelemetry.Exporter.Prometheus.AspNetCore
```

---

## 3. Quick Start (5 minutes)

The minimum setup to get metrics flowing:

**`Program.cs`**
```csharp
var builder = WebApplication.CreateBuilder(args);

// 1. Register observability services
builder.Services.AddEFCoreObservability();

// 2. Register your pooled DbContext with tracking enabled
builder.Services.AddDbContextPool<AppDbContext>((sp, options) =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default"))
           .UseObservability<AppDbContext>(sp, poolSize: 128);
}, poolSize: 128);

var app = builder.Build();

// 3. Activate the DiagnosticListener subscription (MUST be after Build())
app.Services.UseEFCoreObservability();

app.MapGet("/health/pool", (DiagnosticsQueryService q) => q.GetSummary());

app.Run();
```

**Verify it's working:**
```bash
# Make a few requests to trigger pool activity
curl http://localhost:5000/api/your-endpoint

# Check pool metrics
curl http://localhost:5000/health/pool
```

**Expected response:**
```json
{
  "pooled": [{
    "contextName": "AppDbContext",
    "maxPoolSize": 128,
    "physicalCreations": 1,
    "totalRents": 3,
    "totalReturns": 3,
    "activeRents": 0,
    "reuseRatio": 3.0,
    "returnRate": 100.0,
    "leakedContexts": 0,
    "healthStatus": "Healthy"
  }],
  "standard": []
}
```

---

## 4. Full Registration Reference

```csharp
var builder = WebApplication.CreateBuilder(args);

// ── Step 1: Register observability with options ───────────────────────────────
builder.Services.AddEFCoreObservability(opts =>
{
    opts.TrackRentDurations          = true;   // record per-rent timing
    opts.TrackStandardContexts       = true;   // also track non-pooled contexts
    opts.EnableDiagnosticLogging     = false;  // verbose logs (dev only)
    opts.MaxActivityHistoryPerContext = 500;   // ring buffer capacity
    opts.LeakDetectionThresholdMs    = 30_000; // ms before flagging as leak
});

// ── Step 2: Register DbContext with tracking ─────────────────────────────────
builder.Services.AddDbContextPool<PrimaryDbContext>((sp, options) =>
{
    options.UseSqlServer(connectionString)
           .UseObservability<PrimaryDbContext>(sp, poolSize: 128);
}, poolSize: 128);  // poolSize must match in both places

// ── Step 3: Optional OpenTelemetry ───────────────────────────────────────────
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddEFCoreInstrumentation()          // both pool + standard meters
        .AddPrometheusExporter());

// ── Step 4: Build ─────────────────────────────────────────────────────────────
var app = builder.Build();

// ── Step 5: Activate (MUST come after Build) ─────────────────────────────────
app.UseEFCoreObservability();
// OR: app.Services.UseEFCoreObservability();

// ── Step 6: Optional diagnostic endpoint ─────────────────────────────────────
app.MapPrometheusScrapingEndpoint();  // /metrics
app.MapGet("/diagnostics/pool", (DiagnosticsQueryService q) => q.GetSummary());
app.MapGet("/diagnostics/pool/details", (DiagnosticsQueryService q) => q.GetAllDetails());

app.Run();
```

> ⚠️ **`UseEFCoreObservability()` must be called after `builder.Build()`.**  
> The `DiagnosticListener` subscription needs the real singleton `EFCoreDiagnosticObserver` instance from the built service provider. Calling it earlier will subscribe a different (discarded) instance.

---

## 5. Configuring DbContext Types

### 5.1 Pooled DbContext

`UseObservability<TContext>()` does three things internally:
1. Registers pool size with the tracker so `PoolUtilization` can be computed
2. Injects `PoolResettableTrackingService` into EF's internal DI via `TrackingOptionsExtension`
3. Adds `RentTrackingInterceptor` to the command pipeline

```csharp
services.AddDbContextPool<PrimaryDbContext>((sp, options) =>
{
    options.UseSqlServer(connString)
           .UseObservability<PrimaryDbContext>(sp, poolSize: 128);
}, poolSize: 128);
```

> ⚠️ **Pool size must match.** Pass the same value to both `UseObservability<T>(sp, poolSize: N)` and `AddDbContextPool<T>(..., poolSize: N)`. A mismatch will produce incorrect `PoolUtilization` and `RoomToGrow` values.

### 5.2 Standard (Non-Pooled) DbContext

Standard contexts are tracked automatically via `ContextInitialized` / `ContextDisposed` events. No `UseObservability()` call is needed on the options builder — just ensure `TrackStandardContexts = true` in options (it's `true` by default).

```csharp
services.AddDbContext<ReplicaDbContext>(options =>
{
    options.UseSqlServer(replicaConnString);
    // No UseObservability() needed for standard tracking
});
```

If you want standard contexts to also benefit from **rent duration tracking** (as a lifetime metric), you can still call `UseObservability` — it will register the resettable service (no-op for standard contexts) but the diagnostic observer handles the rest.

### 5.3 Multiple DbContext Types

Each context type gets its own isolated metrics bucket automatically — keyed by `typeof(TContext).Name`.

```csharp
// Primary write DB — pooled
services.AddDbContextPool<WriteDbContext>((sp, options) =>
    options.UseSqlServer(writeConn)
           .UseObservability<WriteDbContext>(sp, poolSize: 128), poolSize: 128);

// Read replica — pooled, smaller pool
services.AddDbContextPool<ReadDbContext>((sp, options) =>
    options.UseSqlServer(readConn)
           .UseObservability<ReadDbContext>(sp, poolSize: 64), poolSize: 64);

// Analytics — standard (not pooled)
services.AddDbContext<AnalyticsDbContext>(options =>
    options.UseSqlServer(analyticsConn));
```

Metrics are then independently accessible:
```csharp
diagnosticsQueryService.GetPooledMetrics("WriteDbContext");
diagnosticsQueryService.GetPooledMetrics("ReadDbContext");
diagnosticsQueryService.GetStandardMetrics("AnalyticsDbContext");
```

---

## 6. ObservabilityOptions Reference

```csharp
builder.Services.AddEFCoreObservability(opts => { ... });
```

| Property | Type | Default | Description |
|---|---|---|---|
| `TrackRentDurations` | `bool` | `true` | Record per-rent timing (avg/min/max). Minor overhead. |
| `TrackStandardContexts` | `bool` | `true` | Track non-pooled DbContext creation/disposal. |
| `EnableDiagnosticLogging` | `bool` | `false` | Emit verbose `ILogger` output per event. Use in dev only. |
| `MaxActivityHistoryPerContext` | `int` | `500` | Ring buffer capacity for activity records per context type. |
| `LeakDetectionThresholdMs` | `long` | `30000` | Not currently used for auto-detection; reserved for future anomaly detector. |

---

## 7. OpenTelemetry Integration

### Selective vs. combined registration

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        // Option A: Both pool and standard in one call
        .AddEFCoreInstrumentation()

        // Option B: Only pool metrics
        .AddEFCorePoolInstrumentation()

        // Option C: Only standard metrics
        .AddEFCoreStandardInstrumentation()
    );
```

### Prometheus exporter

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddEFCoreInstrumentation()
        .AddPrometheusExporter());

// In middleware pipeline:
app.MapPrometheusScrapingEndpoint();  // exposes /metrics
```

### Metric names in Prometheus format

OTel converts dot-notation names to underscore format for Prometheus. Example:

| OTel name | Prometheus name |
|---|---|
| `efcore.pool.utilization` | `efcore_pool_utilization_percent` |
| `efcore.pool.rents.total` | `efcore_pool_rents_total` |
| `efcore.pool.leaks` | `efcore_pool_leaks` |
| `efcore.standard.active` | `efcore_standard_active` |

**Sample Prometheus output:**
```
# TYPE efcore_pool_utilization_percent gauge
efcore_pool_utilization_percent{db_context="WriteDbContext"} 7.81

# TYPE efcore_pool_rents_total counter
efcore_pool_rents_total{db_context="WriteDbContext"} 1024

# TYPE efcore_pool_leaks gauge
efcore_pool_leaks{db_context="WriteDbContext"} 0
```

### OTLP exporter (e.g. Grafana Cloud, Datadog)

```csharp
builder.Services.AddOpenTelemetry()
    .WithMetrics(m => m
        .AddEFCoreInstrumentation()
        .AddOtlpExporter(o =>
        {
            o.Endpoint = new Uri("https://otlp.example.com:4317");
            o.Headers = "Authorization=Bearer your-token";
        }));
```

---

## 8. HTTP Diagnostics API

If you don't need OTel, inject `DiagnosticsQueryService` directly into your endpoints or controllers.

### Minimal API endpoints

```csharp
var diag = app.MapGroup("/diagnostics");

// Summary (all context types)
diag.MapGet("/summary", (DiagnosticsQueryService q) => q.GetSummary());

// Full details including activity log
diag.MapGet("/details", (DiagnosticsQueryService q) => q.GetAllDetails());

// Single pooled context
diag.MapGet("/pool/{name}", (string name, DiagnosticsQueryService q) =>
{
    var m = q.GetPooledMetrics(name);
    return m is null ? Results.NotFound() : Results.Ok(m);
});

// Single standard context
diag.MapGet("/standard/{name}", (string name, DiagnosticsQueryService q) =>
{
    var m = q.GetStandardMetrics(name);
    return m is null ? Results.NotFound() : Results.Ok(m);
});

// Recent activity for a context
diag.MapGet("/activity/{name}", (string name, DiagnosticsQueryService q) =>
    q.GetRecentActivity(name, take: 50));
```

### Controller-based

```csharp
[ApiController]
[Route("api/[controller]")]
public class PoolDiagnosticsController : ControllerBase
{
    private readonly DiagnosticsQueryService _diagnostics;

    public PoolDiagnosticsController(DiagnosticsQueryService diagnostics)
        => _diagnostics = diagnostics;

    [HttpGet("summary")]
    public IActionResult GetSummary() => Ok(_diagnostics.GetSummary());

    [HttpGet("pool/{contextName}")]
    public IActionResult GetPooled(string contextName)
    {
        var metrics = _diagnostics.GetPooledMetrics(contextName);
        return metrics is null ? NotFound() : Ok(metrics);
    }

    [HttpGet("activity/{contextName}")]
    public IActionResult GetActivity(string contextName, [FromQuery] int take = 20)
        => Ok(_diagnostics.GetRecentActivity(contextName, take));
}
```

### Response shapes

**`GetSummary()` → `DiagnosticsSummary`:**
```json
{
  "pooled": [
    {
      "contextName": "WriteDbContext",
      "maxPoolSize": 128,
      "physicalCreations": 8,
      "physicalInPool": 8,
      "availableInPool": 2,
      "activeRents": 6,
      "totalRents": 4821,
      "totalReturns": 4815,
      "overflowDisposals": 0,
      "leakedContexts": 0,
      "poolUtilization": 6.25,
      "reuseRatio": 602.6,
      "returnRate": 99.88,
      "avgRentDurationMs": 42.3,
      "minRentDurationMs": 8,
      "maxRentDurationMs": 1240,
      "healthStatus": "Healthy",
      "reuseQuality": "Excellent",
      "lastUpdated": "2026-02-27T10:43:21Z"
    }
  ],
  "standard": []
}
```

---

## 9. Reading Metrics Programmatically

Inject `DiagnosticsQueryService` or `IContextMetricsProvider` anywhere in your application.

### Health check integration

```csharp
builder.Services.AddHealthChecks()
    .Add(new HealthCheckRegistration(
        "efcore-pool",
        sp =>
        {
            var query = sp.GetRequiredService<DiagnosticsQueryService>();
            return new EFCorePoolHealthCheck(query);
        },
        HealthStatus.Degraded,
        tags: ["database", "pool"]));

public class EFCorePoolHealthCheck : IHealthCheck
{
    private readonly DiagnosticsQueryService _query;
    public EFCorePoolHealthCheck(DiagnosticsQueryService query) => _query = query;

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var summary = _query.GetSummary();

        foreach (var pool in summary.Pooled)
        {
            if (pool.LeakedContexts > 0)
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    $"{pool.ContextName}: {pool.LeakedContexts} leaked contexts"));

            if (pool.ReturnRate < 95)
                return Task.FromResult(HealthCheckResult.Degraded(
                    $"{pool.ContextName}: return rate {pool.ReturnRate:F1}%"));
        }

        return Task.FromResult(HealthCheckResult.Healthy("All pools healthy"));
    }
}
```

### Background monitoring service

```csharp
public class PoolMonitorService : BackgroundService
{
    private readonly IContextMetricsProvider _provider;
    private readonly ILogger<PoolMonitorService> _logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var (name, metrics) in _provider.GetAllPooledMetrics())
            {
                if (metrics.LeakedContexts > 0)
                    _logger.LogCritical(
                        "[Pool] LEAK DETECTED: {Context} has {Leaks} leaked contexts",
                        name, metrics.LeakedContexts);

                if (metrics.PoolUtilization > 90)
                    _logger.LogWarning(
                        "[Pool] HIGH UTILIZATION: {Context} at {Util:F1}%",
                        name, metrics.PoolUtilization);
            }

            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
    }
}
```

---

## 10. Prometheus & Grafana Setup

### Prometheus scrape config

```yaml
# prometheus.yml
scrape_configs:
  - job_name: 'myapp-efcore'
    static_configs:
      - targets: ['myapp:8080']
    metrics_path: '/metrics'
    scrape_interval: 15s
```

### Grafana dashboard panels

**Pool health status panel:**
```
# PromQL
efcore_pool_return_rate_percent{db_context="WriteDbContext"}
```
Thresholds: ≥ 99 → green · ≥ 95 → yellow · < 95 → red

**Reuse ratio over time:**
```
# PromQL
efcore_pool_reuse_ratio{db_context="WriteDbContext"}
```

**Active rents (concurrent users of pool):**
```
# PromQL
efcore_pool_rents_active{db_context="WriteDbContext"}
```

**Leak detection (alert panel):**
```
# PromQL
efcore_pool_leaks{db_context="WriteDbContext"} > 0
```

**Rent duration heatmap (avg):**
```
# PromQL
efcore_pool_rent_duration_avg_ms_milliseconds{db_context="WriteDbContext"}
```

---

## 11. Alerting Rules

### Prometheus alerting rules

```yaml
# efcore-alerts.yml
groups:
  - name: efcore_pool
    rules:

      # Critical: Context leaks detected
      - alert: EFCoreContextLeak
        expr: efcore_pool_leaks > 0
        for: 1m
        labels:
          severity: critical
        annotations:
          summary: "DbContext leak in {{ $labels.db_context }}"
          description: "{{ $value }} context(s) rented but never returned. Check for missing `using` statements or exception paths that skip disposal."

      # Warning: Return rate dropped
      - alert: EFCoreReturnRateLow
        expr: efcore_pool_return_rate_percent < 95
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Low pool return rate for {{ $labels.db_context }}"
          description: "Return rate is {{ $value }}%. Expected 100%. Investigate disposal patterns."

      # Warning: Pool near saturation
      - alert: EFCorePoolNearCapacity
        expr: efcore_pool_utilization_percent > 90
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Pool near capacity for {{ $labels.db_context }}"
          description: "Utilization is {{ $value }}%. Consider increasing MaxPoolSize."

      # Warning: Overflow disposals spiking
      - alert: EFCorePoolOverflowing
        expr: rate(efcore_pool_overflow_disposals_total[5m]) > 1
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "Pool overflow for {{ $labels.db_context }}"
          description: "Contexts being disposed due to pool overflow. Increase pool size or reduce concurrency."
```

---

## 12. Validating Your Installation

### Step 1: Check services are registered

```csharp
// In a test or startup validation:
var tracker = app.Services.GetService<DbContextLifeCycleTracker>();
var interceptor = app.Services.GetService<RentTrackingInterceptor>();
var observer = app.Services.GetService<EFCoreDiagnosticObserver>();

Debug.Assert(tracker is not null, "DbContextLifeCycleTracker not registered");
Debug.Assert(interceptor is not null, "RentTrackingInterceptor not registered");
Debug.Assert(observer is not null, "EFCoreDiagnosticObserver not registered");
```

### Step 2: Trigger pool activity

```bash
# Make 10 requests to any endpoint that uses your DbContext
for i in {1..10}; do curl -s http://localhost:5000/api/users > /dev/null; done
```

### Step 3: Verify metrics

```bash
curl http://localhost:5000/diagnostics/summary | jq '.pooled[0]'
```

**What to check:**

| Metric | Expected | Problem if not |
|---|---|---|
| `physicalCreations` | 1–10 (pool warming up) | 0 = observer not subscribed |
| `totalRents` | Equals number of requests | 0 = interceptor not registered |
| `totalReturns` | Should equal `totalRents` when idle | Mismatch = resettable service not wired |
| `activeRents` | 0 when no requests in flight | > 0 at rest = returning contexts too slow |
| `leakedContexts` | 0 | > 0 = real leak or classification bug |
| `reuseRatio` | > 1.0 after first reuse | 1.0 = always creating new contexts |

### Step 4: Simulate a leak (optional, dev only)

```csharp
// DO NOT use in production — this intentionally leaks a context
[HttpGet("test/leak")]
public async Task<IActionResult> SimulateLeak([FromServices] PrimaryDbContext ctx)
{
    // Deliberately NOT disposing ctx — simulates a leak
    var count = await ctx.Users.CountAsync();
    return Ok(new { count, warning = "Context leaked intentionally for testing" });
}
```

After calling this endpoint and waiting for GC, `leakedContexts` should increment to 1.

---

## 13. Troubleshooting

### `totalRents` is always 0 or only equals `physicalCreations`

**Cause:** `RentTrackingInterceptor` is not attached to the context.  
**Fix:** Ensure `UseObservability<TContext>(sp, poolSize)` is called inside the `AddDbContextPool` options builder, and that `sp` is the real service provider (the parameter passed to the lambda), not a captured reference.

```csharp
// ✅ Correct: sp is the parameter, resolved at runtime
services.AddDbContextPool<AppDbContext>((sp, options) =>
    options.UseObservability<AppDbContext>(sp, poolSize: 128));

// ❌ Wrong: builder.Services is the registration-time collection, not a provider
services.AddDbContextPool<AppDbContext>((_, options) =>
    options.UseObservability<AppDbContext>(someEarlyProvider, poolSize: 128));
```

### `physicalCreations` is 0

**Cause:** `UseEFCoreObservability()` was not called after `builder.Build()`, so the `DiagnosticListener` subscription was never activated.  
**Fix:**
```csharp
var app = builder.Build();
app.UseEFCoreObservability();  // ← must be here, not before Build()
```

### `leakedContexts` is non-zero but you don't have leaks

**Cause:** `PoolResettableTrackingService.Configure()` was not called before disposal (context was GC'd without a rental cycle), or `TrackingOptionsExtension` was not applied.  
**Check:** Enable `EnableDiagnosticLogging = true` temporarily and look for `[EFObservability] ResetState called on uninitialized tracking service` in logs.

### `reuseRatio` is always 1.0

**Cause:** Pool size is 1, or requests are never concurrent enough to trigger reuse.  
**Not a bug** for very low-traffic apps. Under load, ratio should climb significantly (100+ for high-traffic endpoints).

### `poolUtilization` is 0 even after requests

**Cause:** Pool size was not registered (tracker does not know `MaxPoolSize`).  
**Fix:** Ensure `poolSize` parameter matches in `UseObservability<TContext>(sp, poolSize: N)` and `AddDbContextPool<TContext>(..., poolSize: N)`.

### OpenTelemetry metrics not appearing in Prometheus

**Check 1:** Is `AddEFCoreInstrumentation()` called before the exporter?
```csharp
.WithMetrics(m => m
    .AddEFCoreInstrumentation()   // ← must come before exporter
    .AddPrometheusExporter())
```

**Check 2:** Is `app.MapPrometheusScrapingEndpoint()` called and the endpoint accessible?

**Check 3:** Have any rents occurred since startup? Observable gauges only appear in the scrape output after the first metric value is recorded.

---

*EFCore.Observability Integration Guide — v1.0*
