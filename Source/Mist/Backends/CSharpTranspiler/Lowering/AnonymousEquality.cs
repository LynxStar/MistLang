using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// <c>==</c> between anonymous values, which Mist reads as VALUE equality where C# reads the reference
/// equality its operator gives. A deliberate divergence rather than a pass-through: two literals of one
/// shape are one type, so comparing them is asking whether they hold the same thing, and C# supplies
/// the <c>Equals</c> that answers it while leaving <c>==</c> alone.
/// <para>
/// Either side being anonymous is enough. The anonymous type is what makes the comparison mean value
/// equality, and <c>object.Equals</c> answers correctly whichever side arrived boxed.
/// </para>
/// </summary>
internal static class AnonymousEquality
{
    public static bool Applies(BinaryOperatorKind kind, IMistType left, IMistType right)
    {
        return kind is BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual
            && (left is AnonymousType || right is AnonymousType)
            ;
    }

    public static string Call(BinaryOperatorKind kind, string left, string right)
    {
        var negation = kind == BinaryOperatorKind.NotEqual ? "!" : "";

        return $"{negation}object.Equals({left}, {right})";
    }
}