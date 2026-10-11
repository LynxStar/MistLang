using System.Globalization;
using System.Text;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The names of the parameterized constructor constraint lowering. C# has only the parameterless
/// form, so <c>where T : new(int)</c> becomes a synthesized interface carrying a static abstract
/// factory, which the type closing the constraint implements and the generic body calls through.
/// <para>
/// A CLOSING TYPE MUST BE DECLARED IN MIST, because the interface is added to its declaration and a
/// precompiled type has no declaration to add to. That is a limit of THIS BACKEND rather than of
/// Mist -- see <see cref="BackendDiagnostics.ForeignConstructorConstraintClosure"/>.
/// </para>
/// <para>
/// THE MEMBER IS ONE OVERLOADED NAME rather than one name per signature, so a parameter carrying
/// both <c>new(int)</c> and <c>new(bool)</c> leaves C# to pick between them by argument type at each
/// <c>new T(...)</c>. Naming them apart would put that matching in this emitter, where two
/// constraints of equal arity cannot be told apart without reimplementing overload resolution.
/// </para>
/// </summary>
internal static class ConstructorConstraintInterfaces
{
    public const string Namespace = "Mist.Emission.CSharp";

    /// <summary>
    /// Mangled so it cannot collide with a <c>New</c> the user declared on the same type.
    /// </summary>
    public const string MemberName = "__New";

    public static string InterfaceName(IReadOnlyList<IMistType> signature)
    {
        return $"__IConstruct_{Suffix(signature)}";
    }

    /// <summary>
    /// The interface as a constraint or base-list entry: fully qualified, because the closing type
    /// and the generic that constrains it may sit in any namespace and the emitted usings cover
    /// neither.
    /// </summary>
    public static string Closed(IReadOnlyList<IMistType> signature, IReadOnlyList<IMistType> closedSignature, string product)
    {
        var open = OpenPositions(signature).Select(i => TypeNameEmitter.Emit(closedSignature[i]));

        return $"{Namespace}.{InterfaceName(signature)}<{string.Join(", ", open.Prepend(product))}>";
    }

    /// <summary>
    /// A position whose type names a type parameter is one of the interface's own parameters rather
    /// than part of its name, since no single interface declaration can spell a parameter that belongs
    /// to whichever generic constrains with it. It is named by its index rather than its type, so every
    /// generic's <c>new(A)</c> is one interface whatever it calls the parameter, and a closing type used
    /// by several appends it once.
    /// </summary>
    public static string TypeParameters(IReadOnlyList<IMistType> signature)
    {
        return string.Join(", ", OpenPositions(signature).Select(i => $"TArg{i}").Prepend("T"));
    }

    public static string DeclaredParameters(IReadOnlyList<IMistType> signature)
    {
        return string.Join(", ", signature.Select((type, i) =>
            $"{(IsOpen(type) ? $"TArg{i}" : TypeNameEmitter.Emit(type))} arg{i}"));
    }

    /// <summary>
    /// The parameter list of the synthesized factory, positional because a constraint names types
    /// without naming parameters.
    /// </summary>
    public static string Parameters(IReadOnlyList<IMistType> signature)
    {
        return string.Join(", ", signature.Select((type, i) => $"{TypeNameEmitter.Emit(type)} arg{i}"));
    }

    public static string Arguments(IReadOnlyList<IMistType> signature)
    {
        return string.Join(", ", signature.Select((_, i) => $"arg{i}"));
    }

    /// <summary>
    /// What a closed position of the interface passes to a constructor written over its definition's own
    /// parameters, as <c>Crate(X held)</c> answers <c>new(int)</c> for <c>Crate of int</c>. C# cannot see
    /// that the closure fixed X to int, so the value is converted through object, which the constraint
    /// rule makes exact: it admits the constructor only when the closing substitution makes the types identical.
    /// </summary>
    public static string Arguments(ConstructorWitness witness)
    {
        return string.Join(", ", witness.Factory.Select((type, i) =>
            !ReferenceEquals(type, witness.Declared[i]) && IsOpen(witness.Declared[i])
                ? $"({TypeNameEmitter.Emit(witness.Declared[i])})(object)arg{i}"
                : $"arg{i}"));
    }

    private static string Suffix(IReadOnlyList<IMistType> signature)
    {
        return string.Join("_", signature.Select((type, i) =>
            IsOpen(type) ? i.ToString(CultureInfo.InvariantCulture) : Identifier(type)));
    }

    private static IEnumerable<int> OpenPositions(IReadOnlyList<IMistType> signature)
    {
        return Enumerable.Range(0, signature.Count).Where(i => IsOpen(signature[i]));
    }

    public static bool IsOpen(IMistType type)
    {
        return TypeParameterReach.Collect(type).Count > 0;
    }

    private static string Identifier(IMistType type)
    {
        var name = TypeNameEmitter.Emit(type);
        var identifier = new StringBuilder(name.Length);

        foreach(var character in name)
        {
            if(char.IsLetterOrDigit(character) || character == '_')
            {
                _ = identifier.Append(character);
            }
        }

        return identifier.ToString();
    }
}