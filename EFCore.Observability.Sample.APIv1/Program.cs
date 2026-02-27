using EFCore.Observability.Extensions;
using EFCore.Observability.Sample.APIv1.Data;
using EFCore.Observability.Services;
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





app.Run();
