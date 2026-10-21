using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A function slot whose declared result is a type parameter, filled where that parameter closes to void, as a
/// void lambda or method group passed to a generic <c>Call</c> taking a function of its <c>R</c>
/// (type-argument-inference Requirement 11.3). Mist sees a function of void, which <see cref="TypeNameEmitter"/>
/// spells Action, but the C# slot is the declaration closed over <c>Standard.Void</c>: a Func that wants a value
/// back. So the function placed there returns the unit instance after its body runs (first-class-functions
/// Requirement 5.5).
/// </summary>
internal static class UnitResultSlots
{
    public static bool Yields(IMistType declared, IMistType closed)
    {
        return declared is IFunctionType open
            && closed is IFunctionType shut
            && OptionalUnion.IsVoid(shut.ReturnType)
            && !OptionalUnion.IsVoid(open.ReturnType);
    }
}