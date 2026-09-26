using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Every refinement predicate a declaration carries -- an alias's, a dependent parameter's, a parameter's
/// `where` -- on a type, in an extension block or on a local function, each with where it is written: the
/// type whose code it counts as, the assembly, how far the declaration is visible, and how a message names
/// it. One enumeration for every rule that judges a predicate where it is declared, so that no such rule
/// reaches a position another misses.
/// </summary>
internal static class DeclaredPredicates
{
    public static IEnumerable<DeclaredPredicate> Of(AnalysisContext context)
    {
        foreach(var declared in context.Types.SelectMany(OfType))
        {
            yield return declared;
        }

        foreach(var method in context.Extensions.SelectMany(block => block.MethodsList))
        {
            var assembly = method.Body is { } body ? context.AssemblyOf(body) : null;
            var site = new PredicateSite(null, assembly, method.Modifiers.Accessibility, "method", method.Name);

            foreach(var declared in OfSignature(method, site))
            {
                yield return declared;
            }
        }

        foreach(var local in context.Nodes.OfType<LocalFunctionStatement>())
        {
            var site = new PredicateSite(
                context.OwnerOf(local), context.AssemblyOf(local), Accessibility.Private, "local function", local.Function.Name);

            foreach(var declared in OfParameters(local.Function.ParametersList, site))
            {
                yield return declared;
            }
        }
    }

    private static IEnumerable<DeclaredPredicate> OfType(MistType type)
    {
        var reach = Declarations.Effective(type);

        if(type is TypeAlias { AliasedType: { } aliased } alias)
        {
            var site = new PredicateSite(alias, alias.Assembly, reach, "type alias", TypeDisplay.Mist(alias));

            foreach(var constituent in aliased is UnionType union ? union.Constituents : [aliased])
            {
                if(constituent is RefinedType { Predicate: { } predicate })
                {
                    yield return new DeclaredPredicate(predicate, TypeDisplay.Mist(alias), site);
                }
            }
        }

        if(type is IHasTypeParameters generic)
        {
            var site = new PredicateSite(type, type.Assembly, reach, "type", type.Name);

            foreach(var declared in OfTypeParameters(generic.TypeParametersList, site))
            {
                yield return declared;
            }
        }

        if(type is not MemberType member)
        {
            yield break;
        }

        foreach(var method in member.EnumerateDelegates())
        {
            var site = new PredicateSite(
                type, type.Assembly, Declarations.Narrowest(method.Modifiers.Accessibility, reach),
                method is Constructor ? "constructor" : "method", method.Name);

            foreach(var declared in OfSignature(method, site))
            {
                yield return declared;
            }
        }

        foreach(var indexer in member.IndexersList)
        {
            var site = new PredicateSite(type, type.Assembly, Declarations.Narrowest(indexer.Modifiers.Accessibility, reach), "indexer", "this");

            foreach(var declared in OfParameters(indexer.ParametersList, site))
            {
                yield return declared;
            }
        }
    }

    private static IEnumerable<DeclaredPredicate> OfSignature(Method method, PredicateSite site)
    {
        return OfTypeParameters(method.TypeParametersList, site).Concat(OfParameters(method.ParametersList, site));
    }

    private static IEnumerable<DeclaredPredicate> OfTypeParameters(IEnumerable<TypeParameter> parameters, PredicateSite site)
    {
        foreach(var parameter in parameters)
        {
            if(parameter.Refinement is { } predicate)
            {
                yield return new DeclaredPredicate(predicate, parameter.Name, site);
            }
        }
    }

    private static IEnumerable<DeclaredPredicate> OfParameters(IEnumerable<Parameter> parameters, PredicateSite site)
    {
        foreach(var parameter in parameters)
        {
            if(parameter.Type is RefinedType { IsAnonymous: true, Predicate: { } predicate })
            {
                yield return new DeclaredPredicate(predicate, parameter.Name, site);
            }
        }
    }
}

internal sealed record DeclaredPredicate(Pattern Predicate, string Constrained, PredicateSite Site);

internal sealed record PredicateSite(MistType? Owner, MistAssembly? Assembly, Accessibility Reach, string Kind, string Name);