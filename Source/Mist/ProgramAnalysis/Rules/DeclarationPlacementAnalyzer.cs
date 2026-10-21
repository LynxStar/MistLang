using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Modifiers the grammar and the builders admit but a finished declaration's position rules out:
/// <c>static</c> on a member of a Mist abstract type (abstract-type-unification Requirement 1.3). A CLR
/// interface's static members are projected, not declared here, and fill no slot (Requirement 14.8).
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
        }
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

    private static void Report(AnalysisContext context, SyntaxNode? at, DiagnosticDescriptor descriptor, params object?[] args)
    {
        if(at is not null)
        {
            context.Report(descriptor, at, [], args);
        }
    }

    internal readonly record struct StaticMember(string Kind, string Name, SyntaxNode? Declaration);
}