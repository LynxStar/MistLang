using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A property or indexer read where it has no getter, or assigned where it has no setter: the properties
/// spec's Requirements 13.1-13.2 and 11.4-11.5. An indexer is judged as its index selected it, never chosen
/// again by accessor, because C# selects first and then refuses -- a sibling that has the accessor does not
/// rescue the one the index picked. A get-only auto-property stays assignable in its own type's
/// constructor body, where the write lands on the backing field; a lambda or local function there is a body
/// of its own and gets no such privilege.
/// </summary>
internal sealed class AccessorAnalyzer : IProgramAnalyzer
{
    public string Id => "operator-and-positional-checks";

    public void Analyze(AnalysisContext context)
    {
        var nested = new HashSet<object>(ReferenceEqualityComparer.Instance);

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case LambdaExpression lambda:
                    nested.UnionWith(Captures.Inside(lambda));
                    break;

                case LocalFunctionStatement function:
                    nested.UnionWith(Captures.Inside(function));
                    break;
            }
        }

        foreach(var node in context.Nodes)
        {
            switch(node)
            {
                case IndexExpression { Indexer: { } indexer } index:
                    Judge(context, index, Named(indexer.DeclaringType, "this[]"), indexer.Getter, indexer.Setter);
                    break;

                case MemberAccessExpression { Member: PropertyResult { Property: var property } } access
                    when !(access.Receiver is ThisExpression && AssignsBackingField(context, access, property, nested)):
                    Judge(context, access, Named(property.DeclaringType, property.Name), property.Getter, property.Setter);
                    break;

                case PropertyReferenceExpression reference when !AssignsBackingField(context, reference, reference.Property, nested):
                    Judge(context, reference, Named(reference.Property.DeclaringType, reference.Property.Name), reference.Property.Getter, reference.Property.Setter);
                    break;
            }
        }
    }

    private static void Judge(
        AnalysisContext context, Expression access, string name, PropertyAccessor? getter, PropertyAccessor? setter)
    {
        var direction = context.DirectionOf(access);

        if(direction.HasFlag(AccessDirection.Read) && getter is null)
        {
            context.Report(ProgramAnalysisDiagnostics.NoGetAccessor, access.Node, [access], name);
        }

        if(direction.HasFlag(AccessDirection.Write) && setter is null)
        {
            context.Report(ProgramAnalysisDiagnostics.NoSetAccessor, access.Node, [access], name);
        }
    }

    private static bool AssignsBackingField(AnalysisContext context, Expression access, Property property, HashSet<object> nested)
    {
        return property is { Getter.IsAuto: true, Setter: null }
            && !nested.Contains(access)
            && context.MemberOf(access) is Constructor { DeclaringType: { } built } constructor
            && ReferenceEquals(built, property.DeclaringType)
            && constructor.Modifiers.IsStatic == property.Modifiers.IsStatic;
    }

    private static string Named(IMistType? declaring, string member)
    {
        return declaring is null ? member : $"{TypeDisplay.Mist(declaring)}.{member}";
    }
}