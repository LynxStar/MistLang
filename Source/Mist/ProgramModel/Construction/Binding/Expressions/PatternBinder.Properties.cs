using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

internal static partial class PatternBinder
{
    /// <summary>
    /// The leading type establishes the receiver before members are resolved; its type test shares
    /// union discrimination and refinement binding with a standalone type pattern. Qualified names
    /// bypass the type-reference resolver, so aliases must be expanded before that test is bound.
    /// </summary>
    private static Pattern? BindPropertyPattern(
        PropertyPatternPrimaryPatternNode node,
        IMistType scrutineeType,
        bool bindingsPermitted,
        BindingContext ctx,
        BuilderContext context)
    {
        var matched = OptionalLifting.NonVoidPortion(scrutineeType, context.ProgramModel.TypeInterner);
        TypeTestPattern? typeTest = null;

        if(node.TypeName is { } typeName)
        {
            var tested = PackPosition.AsSingleType(
                context.Resolver.ResolveNamedType(QualifiedNameHelper.Build(typeName), ctx.Scope, typeName), ctx.Scope, typeName, context.Diagnostics);

            if(tested is TypeAlias alias)
            {
                tested = context.Resolver.ExpandAlias(alias, typeArgs: null);
            }

            tested = CloneConversionBody.Tested(tested, scrutineeType, ctx.Scope);
            tested = OptionalLifting.NonVoidPortion(tested, context.ProgramModel.TypeInterner);
            typeTest = BindTypeTest(node, tested, binding: null, scrutineeType, ctx, context);
            matched = typeTest.NarrowedType;

            if(matched is ErrorType)
            {
                return new PropertyPattern(node, matched, [], typeTest: typeTest);
            }
        }

        var subpatterns = new List<PropertySubpattern>();
        var narrowed = matched;

        foreach(var subpattern in Subpatterns(node.Subpatterns))
        {
            var memberName = subpattern.Name.Lexeme;
            var (resolved, found) = MemberResolution.ResolveStorage(matched, memberName, subpattern, context);

            var memberType = resolved ?? (matched is UnknownType ? UnknownType.Instance : null);

            if(memberType is null)
            {
                if(!MemberResolution.AnswersMembers(matched))
                {
                    return null;
                }

                ReportSubpatternNotStorage(found, matched, memberName, subpattern, context);
                narrowed = ErrorType.Instance;
                continue;
            }

            if(Bind(subpattern.Pattern, memberType, bindingsPermitted, ctx, context) is not { } bound)
            {
                return null;
            }

            subpatterns.Add(new PropertySubpattern(subpattern, memberName, bound, memberType, found));
        }

        var binding = node.Name is { } name
            ? Declare(node, name, matched, bindingsPermitted, ctx, context)
            : null
            ;

        return new PropertyPattern(node, narrowed, subpatterns, binding, typeTest);
    }

    private static void ReportSubpatternNotStorage(
        LookupResult found,
        IMistType matched,
        string memberName,
        SyntaxNode node,
        BuilderContext context)
    {
        if(found is MethodGroupResult or TypeResult or EnumMemberResult)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.PatternSubpatternNotStorage, node, null, matched.Name, memberName);
        }
    }

    private static IReadOnlyList<PropertySubpatternNode> Subpatterns(PropertySubpatternListNode? list)
    {
        return list is null ? [] : GrammarRepetition.LeadAndRepeated(list.PropertySubpattern, list.PropertySubpatterns);
    }
}