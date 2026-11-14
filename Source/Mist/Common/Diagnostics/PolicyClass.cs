namespace Mist.Common.Diagnostics;

/// Fixed rules resist policy changes; Promotable rules allow raising severity; Optional rules also allow enabling or disabling.
/// Policy never lowers severity or hides an admitted occurrence.
public enum PolicyClass
{
    Fixed,
    Promotable,
    Optional,
}