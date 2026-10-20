using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a parameter list as it appears between the parentheses of a declaration.
/// </summary>
internal static class ParameterEmitter
{
    /// <param name="spell">
    /// The name each parameter is declared under, where it is not its own: a local function's
    /// parameter may have been renamed apart from an enclosing local, and the body's references were
    /// renamed with it. A method's parameters are spelled as written.
    /// </param>
    public static string Emit(
        IReadOnlyList<Parameter> parameters, DiagnosticBag diagnostics, Func<Parameter, string>? spell = null)
    {
        if(parameters.Count == 0)
        {
            return "";
        }

        var parts = new List<string>(parameters.Count);
        foreach(var parameter in parameters)
        {
            parts.Add(
                AttributeEmitter.Inline(parameter.DeclaredAttributesList, diagnostics)
                + $"{RefKeywords.For(parameter.Modifier.IsRef, parameter.Modifier.IsReadonly)}"
                + Collects(parameter)
                + $"{SpelledType(parameter, NativeSlotNullability.Declares(parameter))} {spell?.Invoke(parameter) ?? IdentifierSpelling.Of(parameter.Name)}");
        }

        return string.Join(", ", parts);
    }

    internal static string SpelledType(Parameter parameter, bool slotDeclaresNullable)
    {
        var closed = TypeNameEmitter.Closed(parameter.Type);

        return slotDeclaresNullable && !parameter.Modifier.IsRef && TypeNameEmitter.IsReferenceKind(closed)
            ? TypeNameEmitter.EmitOptional(closed)
            : TypeNameEmitter.Emit(parameter.Type);
    }

    /// <summary>
    /// The C# <c>params</c> modifier, for the homogeneous form alone. A value PACK is flattened too
    /// but collects into a tuple, which C# cannot declare that way -- the call site packs its
    /// arguments instead, so declaring it would make the same list flatten twice.
    /// <para>
    /// Position is not checked here. That a params array comes last is a rule of the LANGUAGE --
    /// see <see cref="ProgramModelDiagnostics.ParamsArrayNotLast"/> -- so a
    /// declaration reaching emission has already satisfied it.
    /// </para>
    /// </summary>
    private static string Collects(Parameter parameter)
    {
        return parameter.IsFlattened && parameter.Type is not (TupleType or NamedTupleType)
            ? "params "
            : "";
    }
}