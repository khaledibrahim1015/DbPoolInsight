

namespace EFCore.Observability.Internal;


/// <summary>
/// Encapsulates the logic for determining whether a context disposal is
/// a pool overflow (expected) or a genuine leak (problem).
/// </summary>
internal static class PoolOverflowDetector
{
    /// <summary>
    /// Classifies a disposal event into one of three categories.
    /// </summary>
    public static DisposalClassification Classify(
        InstanceState state,
        long physicalCreations,
        long physicalDisposals,
        int maxPoolSize)
    {
        // If context completed its work and ResetState() was called, EF tried to return it.
        // If the pool was already full at that moment, EF disposed it instead.
        // This is normal overflow — NOT a leak.
        if (state.WasReturnedToPool)
            return DisposalClassification.OverflowAfterReturn;

        // If this instance was created when the pool was already at capacity,
        // it was always an overflow instance (even if an exception prevented return).
        if (state.IsOverflow)
            return DisposalClassification.OverflowCreation;

        // Pool was over capacity when this disposal happened (race condition window).
        bool poolWasOverCapacity = maxPoolSize > 0 &&
            (physicalCreations - physicalDisposals) > maxPoolSize;
        if (poolWasOverCapacity)
            return DisposalClassification.OverflowCapacity;

        // Context was rented but never returned — genuine leak.
        return DisposalClassification.Leaked;
    }
}

internal enum DisposalClassification
{
    /// <summary>Context returned successfully then pool disposed the extra instance.</summary>
    OverflowAfterReturn,

    /// <summary>Context was an overflow instance from creation.</summary>
    OverflowCreation,

    /// <summary>Pool was over capacity during disposal window.</summary>
    OverflowCapacity,

    /// <summary>Context was never returned — a genuine leak.</summary>
    Leaked
}
