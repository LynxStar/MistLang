using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

internal static class UnheldCopies
{
    internal static bool Exists(IMistType from, IMistType to)
    {
        return (Recorded(from, to) ?? Recorded(to, from)) is not null;
    }

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
            DiagnosticSnapshot.Type(from),
            DiagnosticSnapshot.Type(to),
            new NameValue(unheld.Member),
            DiagnosticSnapshot.Type(substitution.Apply(unheld.Declared, interner: null)),
            DiagnosticSnapshot.Type(substitution.Apply(unheld.Renamed, interner: null)),
            DiagnosticSnapshot.Type(source));

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