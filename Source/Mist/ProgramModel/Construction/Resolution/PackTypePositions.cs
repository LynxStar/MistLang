using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Resolution;

/// <summary>
/// What a pack is in the type positions that are not a bare spread (variadics design, "Position
/// Semantics of a Pack Identifier"). Kept beside <see cref="ScopeResolver"/> rather than in it: each
/// is one decision about a pack, taken after the name has been resolved.
/// </summary>
internal static class PackTypePositions
{
    public static IMistType Parenthesized(IMistType inner, TypeInterner interner)
    {
        return inner is SpreadType ? interner.GetOrCreateTupleType([inner]) : inner;
    }

    public static IMistType? Indexed(
        IMistType named, ArrayRankSuffixNode rank, Storm.Runtime.SyntaxNode node, DiagnosticBag diagnostics, Action? reportFailure = null)
    {
        if(named is not TypeParameter { IsPack: true } pack
            || rank is not SizedArrayRankArrayRankSuffixNode sized
            || IntegerLiterals.Length(sized.Size.Lexeme) is not { } index)
        {
            return null;
        }

        if(pack.PackLength is not { } length)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.UnprovenPackIndex, node, null, pack.Name, index);
            reportFailure?.Invoke();
            return ErrorType.Instance;
        }

        if(index >= length)
        {
            diagnostics.ReportAtNode(ProgramModelDiagnostics.PackIndexOutOfBounds, node, null, pack.Name, index, length);
            reportFailure?.Invoke();
            return ErrorType.Instance;
        }

        return pack.ElementAt(index);
    }
}