using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Refuses, at the declaration that introduces it, a pack no specialization binds: C# would name a
/// type parameter not in scope there, and the compiler would report success and hand back errors in
/// a file nobody wrote. One walk over a declaration rather than a report at each member emitter,
/// because a pack can sit anywhere a type can and a repeated guard is one that gets missed.
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
            // declaration per arity, each with ordinary type parameters, or one per instantiation,
            // with none.
            if(parameter.IsPack && Monomorph.InScope() is null && ReferenceEquals(TypeNameEmitter.Closed(parameter), parameter))
            {
                diagnostics.ReportAtNodeTyped(
                    BackendDiagnostics.DependentTypeParameter, node, null,
                    new NameValue(parameter.Name), DiagnosticSnapshot.Type(type, TypeEmitter.DeclaredName(type)));
            }
        }

        foreach(var (member, signatureType, ownParameters) in SignatureTypes(type))
        {
            if(FindUnspecializedPack(signatureType, ownParameters) is { } pack)
            {
                diagnostics.ReportAtNodeTyped(
                    BackendDiagnostics.UnspecializedPack, node, null,
                    BackendFacts.Symbol(type, member, $"{TypeEmitter.DeclaredName(type)}.{member}"), new NameValue(pack.Name));
            }
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
                    $"{TypeEmitter.DeclaredName(type)}({parameter.Name})", parameter.Type, []);
            }
        }
    }

    /// <summary>
    /// The first pack reachable from a type that no specialization binds, or null when every pack
    /// it mentions has a width. A member's OWN pack does not count, since that is bound while the
    /// member's specializations are emitted and this walk runs outside all of them.
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
            || !ReferenceEquals(TypeNameEmitter.Closed(pack), pack)
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
}