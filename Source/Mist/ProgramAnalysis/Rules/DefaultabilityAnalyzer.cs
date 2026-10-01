using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A type that needs a default and has none, and a <c>default</c> of a class or struct with none. A type
/// needs one when it declares a default block or no constructor, its only value then being its default:
/// it is refused at its declaration, naming each field and the base without one, and a <c>default</c> of
/// it is not refused a second time. Order 6 of the analysis register; <see cref="Defaultability"/> is
/// the decision.
/// </summary>
internal sealed class DefaultabilityAnalyzer : IProgramAnalyzer
{
    public string Id => "declaration-shape-checks";

    public void Analyze(AnalysisContext context)
    {
        var refused = new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance);

        foreach(var type in context.Types)
        {
            if(type is ConcreteType { IsIntrinsic: false, CloneSource: null } concrete
                && Defaultability.StatesAZero(concrete)
                && RefuseDeclaration(context, concrete))
            {
                _ = refused.Add(concrete);
            }
        }

        foreach(var node in context.Nodes)
        {
            if(node is DefaultExpression { Type: ConcreteType { IsIntrinsic: false } type } defaulted
                && !refused.Contains(type)
                && !Defaultability.IsDefaultable(type))
            {
                context.Report(ProgramAnalysisDiagnostics.NotDefaultable, defaulted.Node, [defaulted], TypeDisplay.Mist(type));
            }
        }
    }

    private static bool RefuseDeclaration(AnalysisContext context, ConcreteType type)
    {
        var declares = type.DefaultBlock is null ? "no constructor" : "a default block";
        var reported = false;

        foreach(var field in Defaultability.FieldsWithoutDefault(type))
        {
            if((field.DeclarationNode ?? type.DeclarationNode) is { } at)
            {
                context.Report(
                    ProgramAnalysisDiagnostics.FieldHasNoDefault, at, [],
                    field.Name, TypeDisplay.Mist(type), declares, TypeDisplay.Mist(field.Type));
                reported = true;
            }
        }

        if(Defaultability.BaseWithoutDefault(type) is { } baseType && type.DeclarationNode is { } declaration)
        {
            context.Report(
                ProgramAnalysisDiagnostics.BaseNotDefaultable, declaration, [],
                TypeDisplay.Mist(type), declares, TypeDisplay.Mist(baseType));
            reported = true;
        }

        return reported;
    }
}