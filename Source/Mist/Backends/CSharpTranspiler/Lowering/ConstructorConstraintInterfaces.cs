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
    public static string Closed(IReadOnlyList<IMistType> signature, string product)
    {
        return $"{Namespace}.{InterfaceName(signature)}<{product}>";
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

    private static string Suffix(IReadOnlyList<IMistType> signature)
    {
        return string.Join("_", signature.Select(Identifier));
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