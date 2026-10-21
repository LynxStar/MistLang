using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Modifiers the grammar and the builders admit but a finished declaration's position rules out:
/// <c>static</c> on a member of a Mist abstract type (abstract-type-unification Requirement 1.3), whose CLR
/// counterparts are projected rather than declared here and fill no slot (Requirement 14.8), and a bare
/// <c>ref</c> on an operator or cast-operator parameter (ref-parameters Requirement 1.2). <c>readonly ref</c>
/// on such a parameter is deliberately absent: no spec decides it.
/// </summary>
internal sealed class DeclarationPlacementAnalyzer : IProgramAnalyzer
{
    public string Id => "declaration-shape-checks";

    public void Analyze(AnalysisContext context)
    {
        foreach(var type in context.Types)
        {
            if(type is AbstractType abstractType)
            {
                foreach(var member in StaticMembers(abstractType))
                {
                    Report(context, member.Declaration, DeclarationModifierDiagnostics.StaticOnAbstractMember,
                        member.Kind, member.Name, TypeDisplay.Mist(abstractType));
                }
            }

            if(type is ConcreteType { CloneSource: null } concrete)
            {
                foreach(var callable in concrete.OperatorsList.Concat<Method>(concrete.CastOperatorsList))
                {
                    foreach(var parameter in RefParameters(callable))
                    {
                        Report(context, parameter.DeclarationNode, DeclarationModifierDiagnostics.RefOnOperatorParameter,
                            parameter.Name, Describe(callable));
                    }
                }
            }
        }
    }

    public static IEnumerable<Parameter> RefParameters(Method callable)
    {
        return callable is Operator or CastOperator
            ? callable.ParametersList.Where(parameter => parameter.Modifier is { IsRef: true, IsReadonly: false })
            : [];
    }

    public static IEnumerable<StaticMember> StaticMembers(AbstractType type)
    {
        if(type.Modifiers.IsClrProjected)
        {
            yield break;
        }

        foreach(var method in type.MethodsList.Where(method => method.Modifiers.IsStatic))
        {
            yield return new StaticMember("method", method.Name, method.DeclarationNode);
        }

        foreach(var property in type.PropertiesList.Where(property => property.Modifiers.IsStatic))
        {
            yield return new StaticMember("property", property.Name, property.DeclarationNode);
        }

        foreach(var indexer in type.IndexersList.Where(indexer => indexer.Modifiers.IsStatic))
        {
            yield return new StaticMember("indexer", "this[]", indexer.DeclarationNode);
        }
    }

    private static string Describe(Method callable)
    {
        return callable is CastOperator cast
            ? $"the {(cast.IsImplicit ? "implicit" : "explicit")} cast operator to '{TypeDisplay.Mist(cast.TargetType)}'"
            : $"operator '{((Operator)callable).Symbol}'";
    }

    private static void Report(AnalysisContext context, SyntaxNode? at, DiagnosticDescriptor descriptor, params object?[] args)
    {
        if(at is not null)
        {
            context.Report(descriptor, at, [], args);
        }
    }

    internal readonly record struct StaticMember(string Kind, string Name, SyntaxNode? Declaration);
}