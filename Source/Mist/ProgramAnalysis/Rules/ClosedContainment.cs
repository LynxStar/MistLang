using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The containment matrix at the closings a program writes, for the one field a declaration cannot judge:
/// an unconstrained type parameter, admitted in an unrestricted container because nothing says what it
/// holds, until `Box of Resource` puts a linear value in a plain struct. A constrained parameter is NOT
/// judged again here, since its constraint stood in for the field at the declaration and the closing is
/// held to that constraint by <see cref="ConstraintAnalyzer"/> -- `AffineBox of LinearToken` is legal
/// exactly because linear satisfies affine. A construction reached only through a field of a closed one
/// is judged along the way, as the registry records no closing for it; each closed field is reported
/// once, however many sites reach it, because the fault is the closed type's and not the site's. The
/// walk through fields stops at <see cref="Depth"/> constructions because a polymorphically recursive
/// field -- `Node of Pair of T` inside `Node of T` -- reaches a new construction at every step, and
/// no identity set ends that.
/// </summary>
internal static class ClosedContainment
{
    private const int Depth = 8;

    public static void Judge(AnalysisContext context)
    {
        var closures = context.Model.ConstructorConstraintClosures;
        var interner = context.Model.TypeInterner;
        var reported = new HashSet<string>(StringComparer.Ordinal);
        var judged = new HashSet<(IMistType, TypeSubstitution)>();
        var owners = Owners(context.Types);

        foreach(var closing in closures.Closings.Concat(closures.OpenClosings))
        {
            if(!context.IsInSource(closing.Node) || closing.Argument is ErrorType || !owners.TryGetValue(closing.Parameter, out var owner)
                || !judged.Add((owner, closing.Closing)))
            {
                continue;
            }

            switch(owner)
            {
                case ConcreteType container:
                    Judge(context, Closed(container, closing.Closing, interner), closing.Closing, closing.Node, route: "", reported, [], interner);
                    break;

                case TypeAlias { AliasedType: { } aliased }:
                    Nested(context, closing.Closing.Apply(aliased, interner), closing.Node, route: "", reported, [], interner);
                    break;
            }
        }
    }

    private static Dictionary<TypeParameter, MistType> Owners(IReadOnlyList<MistType> types)
    {
        var owners = new Dictionary<TypeParameter, MistType>(ReferenceEqualityComparer.Instance);

        foreach(var type in types)
        {
            var parameters = type switch
            {
                ConcreteType concrete => concrete.TypeParametersList,
                TypeAlias alias => alias.TypeParametersList,
                _ => [],
            };

            foreach(var parameter in parameters)
            {
                owners[parameter] = type;
            }
        }

        return owners;
    }

    private static void Judge(
        AnalysisContext context, IMistType closed, TypeSubstitution closing, Storm.Runtime.SyntaxNode node, string route,
        HashSet<string> reported, HashSet<IMistType> visited, TypeInterner interner)
    {
        if((closed as ConstructedType)?.Definition is not ConcreteType container)
        {
            return;
        }

        foreach(var field in container.FieldsList.Where(field => !field.Modifiers.IsStatic))
        {
            switch(field.Type)
            {
                case TypeParameter parameter when container.TypeParametersList.Contains(parameter) && DisciplineAnalyzer.Constrained(parameter) is null:
                    var argument = closing.Apply(parameter, interner);
                    var held = argument is TypeParameter forwarded
                        ? DisciplineAnalyzer.Constrained(forwarded) ?? SubstructuralDiscipline.None
                        : OwnershipAnalyzer.Substructural(argument);

                    if(DisciplineAnalyzer.Reason(container.Discipline, held) is { } reason && reported.Add($"{closed.FullyQualifiedName}.{field.Name}"))
                    {
                        context.Report(
                            ProgramAnalysisDiagnostics.FieldDisciplineContainment, node, [],
                            new EnumValue(DisciplineAnalyzer.Capitalised(container.Discipline)), DiagnosticSnapshot.Type(closed), new EnumValue(DisciplineAnalyzer.Display(held)), new NameValue(field.Name), new NameValue(reason + route));
                    }

                    break;

                case ConstructedType through when Mentions(through, container.TypeParametersList):
                    Nested(context, closing.Apply(through, interner), node, $"{route}, reached from '{TypeDisplay.Mist(closed)}' through field '{field.Name}'", reported, visited, interner);
                    break;
            }
        }
    }

    private static void Nested(
        AnalysisContext context, IMistType closed, Storm.Runtime.SyntaxNode node, string route,
        HashSet<string> reported, HashSet<IMistType> visited, TypeInterner interner)
    {
        if(closed is ConstructedType construction && visited.Count < Depth && visited.Add(construction))
        {
            Judge(context, construction, TypeSubstitution.ForReceiver(construction), node, route, reported, visited, interner);
        }
    }

    private static IMistType Closed(ConcreteType container, TypeSubstitution closing, TypeInterner interner)
    {
        return interner.GetOrCreateConstructedType(container, container.TypeParametersList.ConvertAll(parameter => closing.Apply(parameter, interner)));
    }

    private static bool Mentions(IMistType type, IReadOnlyList<TypeParameter> parameters)
    {
        return type switch
        {
            TypeParameter parameter => parameters.Contains(parameter),
            ConstructedType constructed => constructed.TypeArguments.Any(argument => Mentions(argument, parameters))
                || (constructed.Enclosing is { } enclosing && Mentions(enclosing, parameters)),
            _ => false,
        };
    }
}