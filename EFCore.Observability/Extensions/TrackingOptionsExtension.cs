
using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Services;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EFCore.Observability.Extensions;


/// <summary>
/// EF Core <see cref="IDbContextOptionsExtension"/> that registers
/// <see cref="PoolResettableTrackingService"/> into the context's internal service provider.
/// This ensures EF Core calls <c>ResetState()</c> when pooled contexts are returned.
/// </summary>
internal sealed class TrackingOptionsExtension : IDbContextOptionsExtension
{
    private readonly IContextMetricsCollector _collector;

    public TrackingOptionsExtension(IContextMetricsCollector collector)
    {
        _collector = collector;
    }

    public DbContextOptionsExtensionInfo Info => new ExtensionInfo(this);

    public void ApplyServices(IServiceCollection services)
    {
        // Register the tracking service; EF Core will call ResetState() on pool returns.
        services.AddScoped<PoolResettableTrackingService>(sp =>
            new PoolResettableTrackingService(_collector));

        // Register as IResettableService so EF Core discovers it automatically.
        services.AddScoped<IResettableService>(sp =>
            sp.GetRequiredService<PoolResettableTrackingService>());
    }

    public void Validate(IDbContextOptions options) { }

    private sealed class ExtensionInfo : DbContextOptionsExtensionInfo
    {
        public ExtensionInfo(IDbContextOptionsExtension extension) : base(extension) { }
        public override bool IsDatabaseProvider => false;
        public override string LogFragment => "TrackingOptionsExtension";
        public override int GetServiceProviderHashCode() => typeof(TrackingOptionsExtension).GetHashCode();
        public override bool ShouldUseSameServiceProvider(DbContextOptionsExtensionInfo other) => other is ExtensionInfo;
        public override void PopulateDebugInfo(IDictionary<string, string> debugInfo)
            => debugInfo["EFCore.Observability:Tracking"] = "enabled";
    }
}
