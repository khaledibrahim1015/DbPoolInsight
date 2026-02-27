

using EFCore.Observability.Core.Abstractions;
using EFCore.Observability.Core.Models;
using EFCore.Observability.Interceptors;
using EFCore.Observability.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace EFCore.Observability.Extensions;

/// <summary>
/// Extension methods for <see cref="DbContextOptionsBuilder"/>.
/// </summary>
public static class OptionsBuilderExtensions
{
    /// <summary>
    /// Enables EFCore.Observability tracking on a pooled <see cref="DbContext"/>.
    /// Must be called inside the options builder delegate passed to
    /// <c>AddDbContextPool</c>.
    ///
    /// <code>
    /// services.AddDbContextPool((sp, options) =>
    /// {
    ///     options.UseSqlServer(connectionString)
    ///            .UseObservability(sp, poolSize: 128);
    /// }, 128);
    /// </code>
    /// By default tracked for standard (non-pooled) contexts as well, but this can be disabled via <see cref="ObservabilityOptions.TrackStandardContexts"/>.
    /// </summary>
    public static DbContextOptionsBuilder UseObservability<TContext>(
        this DbContextOptionsBuilder optionsBuilder,
        IServiceProvider serviceProvider,
        int poolSize = 128)
        where TContext : DbContext
    {
        var collector = serviceProvider.GetRequiredService<IContextMetricsCollector>();
        var interceptor = serviceProvider.GetRequiredService<RentTrackingInterceptor>();

        // Register pool size so utilization % can be computed
        if (collector is DbContextLifeCycleTracker tracker)
            tracker.RegisterPoolSize<TContext>(poolSize);

        // Inject the resettable tracking service into EF's internal DI
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder)
            .AddOrUpdateExtension(new TrackingOptionsExtension(collector));

        // Register the command interceptor for rent tracking
        optionsBuilder.AddInterceptors(interceptor);

        return optionsBuilder;
    }
}
