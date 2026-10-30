namespace Mist.Common.Diagnostics;

/// <summary>
/// How project policy may act on a descriptor: not at all, raise its severity, or switch its rule
/// off and on besides raising it. Nothing in any class can lower a severity or hide an occurrence.
/// </summary>
public enum PolicyClass
{
    Fixed,
    Promotable,
    Optional,
}