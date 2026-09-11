using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// A value crossing between Mist's structural <c>func</c> and a CLR delegate that names the same
/// signature -- <c>Predicate</c>, <c>Comparison</c>, <c>RequestDelegate</c>.
/// <para>
/// Mist treats the two as one type, holding the CLR delegate by the function shape its <c>Invoke</c>
/// describes, so nothing in the model distinguishes the sides. C# does: a delegate converts to
/// another delegate of identical signature only through a CONSTRUCTION, never implicitly. So the
/// crossing is materialised at the sites the emitter already knows a target type for, exactly as
/// <see cref="RegexLowering"/> materialises its own.
/// </para>
/// </summary>
internal static class DelegateLowering
{
    public static bool NeedsConstruction(IMistType source, IMistType? target)
    {
        return target is not null
            && IsNominal(source) != IsNominal(target)
            && DelegateShape.Of(source) is { } from
            && DelegateShape.Of(target) is { } to
            && DelegateShape.SameShape(from, to)
            ;
    }

    private static bool IsNominal(IMistType type)
    {
        return type is not IFunctionType;
    }
}