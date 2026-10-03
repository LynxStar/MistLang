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
        BuilderContext context, bool scrutineeFailed)
    {
        var matched = OptionalLifting.NonVoidPortion(scrutineeType, context.ProgramModel.TypeInterner);
        TypeTestPattern? typeTest = null;

        if(node.TypeName is { } typeName)
        {
            var resolution = context.Resolver.ResolveNamedTypeWithFailure(QualifiedNameHelper.Build(typeName), ctx.Scope, typeName);
            var tested = PackPosition.AsSingleType(
                resolution.Type, ctx.Scope, typeName, context.Diagnostics, () => scrutineeFailed = true);
            scrutineeFailed |= resolution.IsFailed;

            if(tested is TypeAlias alias)
            {
                tested = context.Resolver.ExpandAlias(alias, typeArgs: null);
                scrutineeFailed |= alias.IsFailed;
            }

            tested = CloneConversionBody.Tested(tested, scrutineeType, ctx.Scope);
            tested = OptionalLifting.NonVoidPortion(tested, context.ProgramModel.TypeInterner);
            typeTest = BindTypeTest(node, tested, binding: null, scrutineeType, ctx, context, scrutineeFailed);
            matched = typeTest.NarrowedType;

            scrutineeFailed |= typeTest.IsFailed;
        }

        var subpatterns = new List<PropertySubpattern>();
        var narrowed = matched;
        var reported = false;

        foreach(var subpattern in Subpatterns(node.Subpatterns))
        {
            var memberName = subpattern.Name.Lexeme;
            var lookup = MemberResolution.ResolveStorage(matched, memberName, subpattern, context);
            var (resolved, found) = lookup;
            var memberFailed = lookup.Reported;

            var memberType = resolved ?? (matched is UnknownType ? UnknownType.Instance : matched is ErrorType ? ErrorType.Instance : null);

            if(memberType is null)
            {
                if(!MemberResolution.AnswersMembers(matched))
                {
                    return null;
                }

                memberFailed |= ReportSubpatternNotStorage(found, matched, memberName, subpattern, context);
                reported |= memberFailed;
                narrowed = ErrorType.Instance;
                memberType = ErrorType.Instance;
            }

            if(Bind(subpattern.Pattern, memberType, bindingsPermitted, ctx, context, scrutineeFailed || memberFailed) is not { } bound)
            {
                return null;
            }

            subpatterns.Add(new PropertySubpattern(subpattern, memberName, bound, memberType, found));
        }

        var binding = node.Name is { } name
            ? Declare(node, name, matched, bindingsPermitted, ctx, context, scrutineeFailed)
            : null
            ;

        return new PropertyPattern(node, narrowed, subpatterns, binding, typeTest) { IsFailed = scrutineeFailed || reported || typeTest?.IsFailed is true || subpatterns.Any(subpattern => subpattern.Pattern.IsFailed) };
    }

    private static bool ReportSubpatternNotStorage(
        LookupResult found,
        IMistType matched,
        string memberName,
        SyntaxNode node,
        BuilderContext context)
    {
        if(found is MethodGroupResult or TypeResult)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.PatternSubpatternNotStorage, node, null, matched.Name, memberName);
            return true;
        }

        return false;
    }

    private static IReadOnlyList<PropertySubpatternNode> Subpatterns(PropertySubpatternListNode? list)
    {
        return list is null ? [] : GrammarRepetition.LeadAndRepeated(list.PropertySubpattern, list.PropertySubpatterns);
    }
}