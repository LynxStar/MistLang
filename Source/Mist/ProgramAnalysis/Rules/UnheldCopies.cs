using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A newtype whose copy from its source cannot be field for field, because a field names the source
/// and so stands for the newtype in the clone, and whose source defines no <c>Clone</c> to convert
/// through instead (newtypes spec Requirement 10.3). Neither direction exists, so the refusal is owed
/// wherever one was asked for -- a cast, and a position that expected the other type -- and it names
/// the field, which is the only part of the declaration the author has to change.
/// </summary>
internal static class UnheldCopies
{
    public static bool Report(AnalysisContext context, IMistType from, IMistType to, SyntaxNode at, IReadOnlyList<Expression> about)
    {
        if((Recorded(from, to) ?? Recorded(to, from)) is not { } found)
        {
            return false;
        }

        var (instance, unheld, source) = found;
        var substitution = TypeSubstitution.ForReceiver(instance as ConstructedType);

        context.Report(
            ProgramAnalysisDiagnostics.CopyConversionDoesNotHold,
            at,
            about,
            TypeDisplay.Mist(from),
            TypeDisplay.Mist(to),
            unheld.Member,
            TypeDisplay.Mist(substitution.Apply(unheld.Declared, interner: null)),
            TypeDisplay.Mist(substitution.Apply(unheld.Renamed, interner: null)),
            TypeDisplay.Mist(source));

        return true;
    }

    private static (IMistType Instance, UnheldCopy Unheld, IMistType Source)? Recorded(IMistType clone, IMistType other)
    {
        var definition = clone is ConstructedType constructed ? constructed.Definition : clone;

        return definition is ConcreteType { UnheldCopy: { } unheld }
            && ConversionRelation.CloneSourceOf(clone) is { } source
            && ConversionRelation.IsIdentity(other, source)
            ? (clone, unheld, source)
            : null;
    }
}