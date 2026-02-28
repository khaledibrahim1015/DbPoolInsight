using EFCore.Observability.API.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.


builder.Services.AddDbContext<PrimaryDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("TMS_Conn")));
builder.Services.AddDbContext<ReplicaDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("TMSReplica_Conn")));


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Configure the HTTP request pipeline.

    app.UseSwagger();
    app.UseSwaggerUI();


app.UseHttpsRedirection();

app.UseAuthorization();
app.MapControllers();


app.MapGet("/ping", () => Results.Ok(new
{
    Message = "API is running",
    Environment = app.Environment.EnvironmentName,
    Time = DateTime.UtcNow,
    Version = "1.0"
}));
app.MapPost("/bills", async (PrimaryDbContext dbContext, Bill bill) =>
{
    dbContext.Bills.Add(bill);
    await dbContext.SaveChangesAsync();
    return Results.Created($"/bills/{bill.Id}", bill);
});




RunDatabaseMigrations(app);



app.Run();
void RunDatabaseMigrations(WebApplication app)
{
    using var scope = app.Services.CreateScope();
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();

    try
    {
        // Apply migrations for PrimaryDbContext
        var primaryContext = services.GetRequiredService<PrimaryDbContext>();
        logger.LogInformation("Applying migrations for PrimaryDbContext...");
        primaryContext.Database.Migrate();
        logger.LogInformation("PrimaryDbContext migrations applied successfully.");
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "An error occurred while applying database migrations.");
        throw;
    }
}