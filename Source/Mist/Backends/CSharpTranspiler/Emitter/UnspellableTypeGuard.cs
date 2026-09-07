using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Refuses the two type shapes this backend has no C# spelling for, at the declaration that
/// introduces them.
/// <para>
/// Both reach <see cref="TypeNameEmitter.Emit"/>'s fallback arm today and are written verbatim, so
/// the generated file does not parse: a refinement emits its own display name, braces included, and
/// a dependent VALUE parameter emits as a C# TYPE parameter that every body use then fails against.
/// Neither produced a Mist diagnostic, which is the failure this project holds to be worse than an
/// unbuilt feature -- the compiler reports success and hands back errors in a file nobody wrote.
/// </para>
/// <para>
/// One walk over a declaration rather than a report at each emitter, because a refinement is legal
/// in every type position and five member emitters would each have to remember it. A guard that has
/// to be repeated is a guard that gets missed, which is how the ref-parameter check came to rest on
/// a coincidence.
/// </para>
/// </summary>
internal static class UnspellableTypeGuard
{
    public static void Report(MemberType type, DiagnosticBag diagnostics)
    {
        if(type.DeclarationNode is not { } node)
        {
            return;
        }

        foreach(var parameter in type.TypeParametersList)
        {
            // A pack is spelled by SPECIALIZING rather than by carrying a value, so it is exempt
            // from the witness encoding a dependent parameter needs: emission writes one C#
            // declaration per arity, and each of those has ordinary type parameters.
            if(parameter.IsPack && Monomorph.InScope() is null)
            {
                diagnostics.ReportAtNode(
                    BackendDiagnostics.DependentTypeParameter, node, null,
                    parameter.Name, TypeEmitter.CleanName(type));
            }
        }

        foreach(var (member, signatureType, ownParameters) in SignatureTypes(type))
        {
            if(FindRefinement(signatureType) is { } refined)
            {
                diagnostics.ReportAtNode(
                    BackendDiagnostics.RefinedTypeInSignature, node, null,
                    $"{TypeEmitter.CleanName(type)}.{member}", refined.BaseType.Name);
            }

            if(FindUnspecializedPack(signatureType, ownParameters) is { } pack)
            {
                diagnostics.ReportAtNode(
                    BackendDiagnostics.UnspecializedPack, node, null,
                    $"{TypeEmitter.CleanName(type)}.{member}", pack.Name);
            }
        }
    }

    /// <summary>
    /// The same refusal for a LOCAL, which the declaration walk structurally cannot reach: a
    /// refinement ALIAS declares no type of its own to guard, so a program that only ever names one
    /// on a local -- which is what a comparison sample does -- has nothing for the walk to find.
    /// </summary>
    public static void ReportLocal(
        LocalDeclarationStatement declaration, DiagnosticBag diagnostics)
    {
        if(declaration.Initializer?.Node is { } node
            && FindRefinement(declaration.Local.Type) is { } refined)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.RefinedTypeInSignature, node, null,
                declaration.Local.Name, refined.BaseType.Name);
        }
    }

    private readonly record struct Signature(
        string Member, IMistType Type, IReadOnlyList<TypeParameter> OwnParameters);

    private static IEnumerable<Signature> SignatureTypes(MemberType type)
    {
        foreach(var method in type.MethodsList)
        {
            yield return new Signature(method.Name, method.ReturnType, method.TypeParametersList);

            foreach(var parameter in method.ParametersList)
            {
                yield return new Signature(
                    $"{method.Name}({parameter.Name})", parameter.Type, method.TypeParametersList);
            }
        }

        foreach(var property in type.PropertiesList)
        {
            yield return new Signature(property.Name, property.Type, []);
        }

        foreach(var indexer in type.IndexersList)
        {
            yield return new Signature("this[]", indexer.Type, []);

            foreach(var parameter in indexer.ParametersList)
            {
                yield return new Signature($"this[{parameter.Name}]", parameter.Type, []);
            }
        }

        if(type is not ConcreteType concrete)
        {
            yield break;
        }

        foreach(var field in concrete.FieldsList)
        {
            yield return new Signature(field.Name, field.Type, []);
        }

        foreach(var constructor in concrete.ConstructorsList)
        {
            foreach(var parameter in constructor.ParametersList)
            {
                yield return new Signature(
                    $"{TypeEmitter.CleanName(type)}({parameter.Name})", parameter.Type, []);
            }
        }
    }

    /// <summary>
    /// The first pack reachable from a type that no specialization binds, or null when every pack
    /// it mentions has a width. A member's OWN pack does not count, since that is bound while the
    /// member's specializations are emitted and this walk runs outside all of them.
    /// <para>
    /// Written as one walk over the signature for the same reason the refinement check is: a pack
    /// can sit anywhere a type can, and a check repeated at each member emitter is one that gets
    /// missed at the next. What it guards against is the failure this project holds worst -- C#
    /// naming a type parameter that is not in scope there, which reads as a bug in generated code
    /// rather than as an unbuilt feature.
    /// </para>
    /// </summary>
    private static TypeParameter? FindUnspecializedPack(
        IMistType type, IReadOnlyList<TypeParameter> ownParameters)
    {
        return FindUnspecializedPack(type, ownParameters, new HashSet<IMistType>());
    }

    private static TypeParameter? FindUnspecializedPack(
        IMistType type, IReadOnlyList<TypeParameter> ownParameters, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            return null;
        }

        if(Unbound(type, ownParameters) is { } unbound)
        {
            return unbound;
        }

        return type switch
        {
            SpreadType spread => FindUnspecializedPack(spread.Operand, ownParameters, seen),
            ConstructedType constructed => FindFirstPack(constructed.TypeArguments, ownParameters, seen),
            UnionType union => FindFirstPack(union.Constituents, ownParameters, seen),
            NamedTupleType named => FindFirstPack(named.ElementTypes, ownParameters, seen),
            TupleType tuple => FindFirstPack(tuple.ElementTypes, ownParameters, seen),
            FunctionType function => FindUnspecializedPack(function.ReturnType, ownParameters, seen)
                ?? FindFirstPack(function.ParameterTypes, ownParameters, seen),
            _ => null,
        };
    }

    private static TypeParameter? Unbound(IMistType type, IReadOnlyList<TypeParameter> ownParameters)
    {
        TypeParameter? pack = null;

        if(type is SpreadType spread && spread.Operand is TypeParameter operand)
        {
            pack = operand;
        }
        else if(type is TypeParameter { IsPack: true } parameter)
        {
            pack = parameter;
        }

        if(pack is null)
        {
            return null;
        }

        var bound = Monomorph.InScope()?.Binds(pack) == true
            || ownParameters.Any(own => ReferenceEquals(own, pack));

        return bound ? null : pack;
    }

    private static TypeParameter? FindFirstPack(
        IReadOnlyList<IMistType> types, IReadOnlyList<TypeParameter> ownParameters, HashSet<IMistType> seen)
    {
        foreach(var type in types)
        {
            if(FindUnspecializedPack(type, ownParameters, seen) is { } pack)
            {
                return pack;
            }
        }

        return null;
    }

    /// <summary>
    /// The first refinement reachable from a type, descending every composite
    /// <see cref="TypeNameEmitter.Emit"/> descends. A refinement nested inside one is as unspellable
    /// as a bare one -- <c>List&lt;Age&gt;</c> emits the same broken name in its argument position.
    /// </summary>
    private static RefinedType? FindRefinement(IMistType type)
    {
        return FindRefinement(type, new HashSet<IMistType>());
    }

    private static RefinedType? FindRefinement(IMistType type, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            return null;
        }

        return type switch
        {
            RefinedType refined => refined,
            ConstructedType constructed => FindFirst(constructed.TypeArguments, seen),
            UnionType union => FindFirst(union.Constituents, seen),
            NamedTupleType named => FindFirst(named.ElementTypes, seen),
            TupleType tuple => FindFirst(tuple.ElementTypes, seen),
            FunctionType function => FindRefinement(function.ReturnType, seen)
                ?? FindFirst(function.ParameterTypes, seen),
            _ => null,
        };
    }

    private static RefinedType? FindFirst(IReadOnlyList<IMistType> types, HashSet<IMistType> seen)
    {
        foreach(var type in types)
        {
            if(FindRefinement(type, seen) is { } refined)
            {
                return refined;
            }
        }

        return null;
    }
}