using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The places that would hold a ref struct off the stack: storage <see cref="RefStructPositions"/> refuses,
/// a type argument, a union constituent, an optional, and a tuple or array element. A composite is judged
/// where it was written -- an alias's union once, at the alias -- and an inferred tuple or array literal
/// where it is formed, unless a written target gave it its type. A ref struct local, parameter or return
/// stays admitted, and so does a ref struct declaring an interface: the boxing that would follow is refused
/// where a value is converted, by <see cref="Assignability.Boxing"/>.
/// </summary>
internal sealed class RefStructPositionAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        foreach(var type in context.Types.OfType<MemberType>())
        {
            JudgeStorage(context, type);
        }

        foreach(var closing in context.Model.ConstructorConstraintClosures.Closings)
        {
            if(RefStructs.Is(closing.Argument) && context.IsInSource(closing.Node))
            {
                context.Report(ProgramAnalysisDiagnostics.RefStructTypeArgument, closing.Node, [], TypeDisplay.Mist(closing.Argument));
            }
        }

        foreach(var application in context.Model.OptionalApplications.Applications)
        {
            if(RefStructs.Is(application.Inner) && context.IsInSource(application.Node))
            {
                context.Report(ProgramAnalysisDiagnostics.RefStructOptional, application.Node, [], TypeDisplay.Mist(application.Inner));
            }
        }

        foreach(var application in context.Model.CompositeApplications)
        {
            foreach(var part in application.Parts.Where(RefStructs.Is))
            {
                Report(context, application.Kind, part, application.Node, []);
            }
        }

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case TupleLiteralExpression { IsTargetTyped: false } tuple when tuple.Elements.FirstOrDefault(element => RefStructs.Is(element.Type)) is { } held:
                    Report(context, CompositeKind.Tuple, held.Type, tuple.Node, [tuple]);
                    break;

                case ArrayLiteralExpression { IsTargetTyped: false } array when RefStructs.Is(array.ElementType):
                    Report(context, CompositeKind.Array, array.ElementType, array.Node, [array]);
                    break;
            }
        }
    }

    private static void JudgeStorage(AnalysisContext context, MemberType container)
    {
        foreach(var field in (container as ConcreteType)?.FieldsList ?? [])
        {
            if(RefStructPositions.Field(container, field) is { } refusal && (field.DeclarationNode ?? Declarations.NodeOf(container)) is { } at)
            {
                context.Report(refusal, at, [], TypeDisplay.Mist(field.Type), container.Name);
            }
        }

        foreach(var property in container.PropertiesList)
        {
            if(RefStructPositions.Property(container, property) is { } refusal && (property.DeclarationNode ?? Declarations.NodeOf(container)) is { } at)
            {
                context.Report(refusal, at, [], TypeDisplay.Mist(property.Type), container.Name);
            }
        }
    }

    private static void Report(AnalysisContext context, CompositeKind kind, IMistType held, SyntaxNode at, IReadOnlyList<Expression> about)
    {
        if(!context.IsInSource(at))
        {
            return;
        }

        if(kind == CompositeKind.Union)
        {
            context.Report(ProgramAnalysisDiagnostics.RefStructUnionConstituent, at, about, TypeDisplay.Mist(held));
            return;
        }

        var (position, reason) = RefStructPositions.Element(kind);
        context.Report(ProgramAnalysisDiagnostics.RefStructElement, at, about, TypeDisplay.Mist(held), position, reason);
    }
}