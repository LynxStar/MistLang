namespace Mist.Semantics.Model;

/// <summary>
/// The original CLR calling convention for a parameter projected as Mist ref.
/// Used by the emitter to choose between emitting 'ref', 'in', or 'out' at the C# call site.
/// </summary>
public enum ClrCallingConvention
{
    /// <summary>Standard CLR ref parameter — emit 'ref' at call site.</summary>
    Ref,

    /// <summary>CLR in parameter — emit 'in' at call site.</summary>
    In,

    /// <summary>CLR out parameter — emit 'out' at call site.</summary>
    Out,
}