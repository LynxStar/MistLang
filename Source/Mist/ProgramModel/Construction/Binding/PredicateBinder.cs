using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Binds every refinement predicate the program declares -- a refined alias's, a dependent type
/// parameter's `where`, a parameter's `where` -- ONCE, onto the model, before any body is bound.
/// A predicate is written in one place and read from many: every `is` against the alias, every
/// assignment into it, every index into an array. Bound here it is one pattern those sites share;
/// bound at each site it would be re-resolved and, when malformed, complained of once per use.
/// </summary>
internal static class PredicateBinder
{
    public static void BindAll(Model.ProgramModel programModel, BuilderContext context)
    {
        var seen = new HashSet<MistType>(ReferenceEqualityComparer.Instance);

        foreach(var type in programModel.TypesByFQN.Values.OfType<MistType>().SelectMany(AndNested))
        {
            if(!seen.Add(type))
            {
                continue;
            }

            if(type is TypeAlias { AliasedType: { } aliased } alias)
            {
                foreach(var refined in Refinements(aliased))
                {
                    refined.Predicate ??= PatternBinder.BindRefinement(refined, Context(alias.Scope), context);
                }
            }

            if(type is IHasTypeParameters generic)
            {
                BindTypeParameters(generic.TypeParametersList, type.Scope, context);
            }

            if(type is MemberType member)
            {
                foreach(var method in member.EnumerateDelegates())
                {
                    BindTypeParameters(method.TypeParametersList, method.Scope, context);
                    BindParameters(method.ParametersList, method.Scope, context);
                }

                foreach(var indexer in member.IndexersList)
                {
                    BindParameters(indexer.ParametersList, type.Scope, context);
                }
            }
        }
    }

    private static void BindTypeParameters(IEnumerable<TypeParameter> parameters, Scope scope, BuilderContext context)
    {
        foreach(var parameter in parameters)
        {
            if(parameter.RefinementNode is AndPatternNode predicate && parameter.Refinement is null)
            {
                parameter.Refinement = PatternBinder.BindPredicate(
                    predicate, parameter.Domain ?? context.Resolver.LookupWellKnown(WellKnownTypes.Int32), Context(scope), context);
            }
        }
    }

    private static void BindParameters(IEnumerable<Parameter> parameters, Scope scope, BuilderContext context)
    {
        foreach(var parameter in parameters)
        {
            if(parameter.RefinementNode is AndPatternNode predicate && parameter.Refinement is null)
            {
                parameter.Refinement = PatternBinder.BindPredicate(predicate, parameter.Type, Context(scope), context);
            }
        }
    }

    private static BindingContext Context(Scope scope)
    {
        return new BindingContext(new BodyScope(scope), returnType: null);
    }

    private static IEnumerable<MistType> AndNested(MistType type)
    {
        yield return type;

        foreach(var nested in type.NestedTypes.SelectMany(AndNested))
        {
            yield return nested;
        }
    }

    private static IEnumerable<RefinedType> Refinements(IMistType type)
    {
        return type switch
        {
            RefinedType refined => [refined],
            UnionType union => union.Constituents.OfType<RefinedType>(),
            _ => [],
        };
    }
}