using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Maps ProgramModel types to their C# source representation.
/// </summary>
internal static class TypeNameEmitter
{
    /// <summary>
    /// The C# spelling of a Mist type.
    /// <para>
    /// A dependent type argument emits its DOMAIN, whether or not its value is known. Both
    /// carriers would otherwise emit something that is not a C# type -- a <see cref="LiftedType"/>
    /// is named for the value it holds, so the fallback arm would emit the <c>3</c> of
    /// <c>Array&lt;int, 3&gt;</c>, and an <see cref="UnknownValueType"/> is named <c>unknown</c>.
    /// C# has neither singleton types nor a spelling for "some int I cannot name", so the domain
    /// is the only faithful projection of either. Reached whenever a dependent argument becomes an
    /// expression's type, which today means an array length read through <c>arr.Length</c>.
    /// </para>
    /// <para>
    /// <see cref="UnknownType"/> is the separate case and keeps emitting <c>object</c>: it is the
    /// user-facing top type, with no domain to fall back on.
    /// </para>
    /// </summary>
    public static string Emit(IMistType type)
    {
        return type switch
        {
            ConstructedType constructed => EmitConstructed(constructed),
            LiftedType lifted => Emit(lifted.Type),
            UnknownValueType unknownValue => Emit(unknownValue.Domain),
            UnionType union => EmitUnion(union),
            NamedTupleType named => EmitTuple(named.ElementTypes, named.ElementNames),
            TupleType tuple => EmitTuple(tuple.ElementTypes, elementNames: null),
            FunctionType function => EmitFunction(function),
            MemberType memberType => MapMemberType(memberType),
            ErrorType => "/* error */",
            UnknownType => "object",
            _ => type.Name,
        };
    }

    /// <summary>
    /// The C# spelling of a tuple where a PATTERN is expected, which is NOT the spelling
    /// <see cref="Emit"/> gives it. C# reads <c>(int, string)</c> in pattern position as a positional
    /// pattern rather than as a type, so a type test emitted through <see cref="Emit"/> silently becomes
    /// a deconstruction OF the scrutinee -- and fails outright on an <c>unknown</c>, which has no
    /// Deconstruct. Naming the ValueTuple is the only spelling C# reads as a type here. Anything that is
    /// not a tuple is spelled as it always is.
    /// </summary>
    internal static string EmitAsPatternType(IMistType type)
    {
        var elementTypes = type switch
        {
            NamedTupleType named => named.Underlying.ElementTypes,
            TupleType tuple => tuple.ElementTypes,
            _ => null,
        };

        return elementTypes is null
            ? Emit(type)
            : $"System.ValueTuple<{string.Join(", ", elementTypes.Select(Emit))}>"
            ;
    }

    /// <summary>
    /// The C# spelling of "a T, or absent": Nullable of T for a value kind, and the bare type for a
    /// reference kind with null standing for absence. Reference kinds are left UNANNOTATED because the
    /// generated files carry no nullable context, where a <c>?</c> on a reference type is a warning.
    /// </summary>
    internal static string EmitOptional(IMistType type)
    {
        var name = Emit(type);

        return IsValueKind(type) ? $"{name}?" : name;
    }

    /// <summary>
    /// An optional (<c>T | void</c>) bypasses the carrier and lowers to the spelling above. Every other
    /// union is spelled as its generated carrier struct, whose name <see cref="UnionCarrierNames"/> owns.
    /// </summary>
    private static string EmitUnion(UnionType union)
    {
        return OptionalUnion.InnerType(union) is { } inner
            ? EmitOptional(inner)
            : UnionCarrierNames.CarrierName(union)
            ;
    }

    internal static bool IsValueKind(IMistType type)
    {
        return type switch
        {
            StructType => true,
            EnumType => true,
            TupleType => true,
            NamedTupleType => true,
            RefinedType refined => IsValueKind(refined.BaseType),
            ConstructedType constructed => IsValueKind(constructed.Definition),
            _ => false,
        };
    }

    /// <summary>
    /// A void result picks Action and any other picks Func, which is the CLR drawing a distinction Mist
    /// does not: void is an ordinary unit type here, so <c>func&lt;int, void&gt;</c> and
    /// <c>func&lt;int, bool&gt;</c> differ only in their result, while <c>Action&lt;int&gt;</c> and
    /// <c>Func&lt;int, bool&gt;</c> are unrelated families. Arity does the rest -- neither family spells
    /// its no-argument case with an empty argument list.
    /// </summary>
    private static string EmitFunction(FunctionType function)
    {
        var parameters = function.ParameterTypes.Select(Emit).ToList();

        if(OptionalUnion.IsVoid(function.ReturnType))
        {
            return parameters.Count == 0
                ? "System.Action"
                : $"System.Action<{string.Join(", ", parameters)}>"
                ;
        }

        parameters.Add(Emit(function.ReturnType));
        return $"System.Func<{string.Join(", ", parameters)}>";
    }

    private static string EmitTuple(IReadOnlyList<IMistType> elementTypes, IReadOnlyList<string?>? elementNames)
    {
        var parts = new string[elementTypes.Count];

        for(var i = 0; i < elementTypes.Count; i++)
        {
            var spelled = Emit(elementTypes[i]);

            parts[i] = elementNames is not null && i < elementNames.Count && elementNames[i] is { } name
                ? $"{spelled} {name}"
                : spelled
                ;
        }

        return $"({string.Join(", ", parts)})";
    }

    private static string EmitConstructed(ConstructedType constructed)
    {
        if(SlotLift.Erased(constructed) is { } erased)
        {
            return Emit(erased);
        }

        if(ClrProjection.Name(constructed.Definition) is { } projected)
        {
            return $"{projected}<{string.Join(", ", constructed.TypeArguments.Select(Emit))}>";
        }

        // Standard.Array<ElementType, Length> → elementType[]
        if(constructed.Definition.FullyQualifiedName == WellKnownTypes.Array.FullyQualifiedName)
        {
            var elementType = Emit(constructed.TypeArguments[0]);
            return $"{elementType}[]";
        }

        // A construction reaching `unknown` anywhere in its arguments is spelled `dynamic`, NOT
        // `Definition<object, ...>`. Mist admits `MyList<int>` where `MyList<unknown>` is expected
        // because unknown is the top type, and C# class generics are invariant, so the `object`
        // spelling is a type Roslyn refuses to convert into. `dynamic` erases to object with the
        // conversion allowed and dispatches members against the runtime type instead -- which is why
        // the member filtering that makes this sound is a COMPILE-TIME guarantee with nothing
        // enforcing it downstream. A BARE unknown is unaffected and stays `object`.
        if(ContainsUnknown(constructed))
        {
            return "dynamic";
        }

        // Generic types: Definition<Arg1, Arg2, ...>
        var baseName = MistType.StripArity(constructed.Definition.Name);
        var args = string.Join(", ", constructed.TypeArguments.Select(Emit));

        if(constructed.Definition is MemberType { DeclaringType: MemberType declaring })
        {
            return $"{Qualified(declaring)}.{baseName}<{args}>";
        }

        return $"{baseName}<{args}>";
    }

    private static bool ContainsUnknown(IMistType type)
    {
        return type switch
        {
            UnknownType => true,
            ConstructedType constructed => constructed.TypeArguments.Any(ContainsUnknown),
            UnionType union => union.Constituents.Any(ContainsUnknown),
            ITupleType tuple => tuple.ElementTypes.Any(ContainsUnknown),
            FunctionType function =>
                function.ParameterTypes.Any(ContainsUnknown) || ContainsUnknown(function.ReturnType),
            _ => false,
        };
    }

    private static string MapMemberType(MemberType memberType)
    {
        // Map well-known Standard namespace types to C# keywords.
        if(memberType.NamespaceName == "Standard")
        {
            return memberType.Name switch
            {
                "Int32" => "int",
                "Int64" => "long",
                "UInt32" => "uint",
                "UInt64" => "ulong",
                "Int16" => "short",
                "UInt16" => "ushort",
                "Byte" => "byte",
                "SByte" => "sbyte",
                "Single" => "float",
                "Double" => "double",
                "Decimal" => "decimal",
                "Boolean" => "bool",
                "Char" => "char",
                "String" => "string",

                // Standard.Void is a real emitted struct (__Void.g.cs), because System.Void
                // is unusable in every position a unit type needs. The one place the C#
                // `void` keyword appears is a method's return type, and that exception is
                // MethodEmitter.EmitSignature's, not this map's.
                "Void" => "Standard.Void",
                _ => memberType.Name,
            };
        }

        if(ShouldQualify(memberType))
        {
            return Qualified(memberType);
        }

        return memberType.Name;
    }

    /// <summary>
    /// Built segment by segment because <see cref="IMistType.FullyQualifiedName"/> keeps the arity
    /// marker a generic segment is registered under, leaving a nesting path that is not a C# name.
    /// </summary>
    private static string Qualified(MemberType type)
    {
        var segment = type.TypeParametersList.Count > 0
            ? $"{MistType.StripArity(type.Name)}<{string.Join(", ", type.TypeParametersList.Select(p => p.Name))}>"
            : type.Name;

        if(type.DeclaringType is MemberType declaring)
        {
            return $"{Qualified(declaring)}.{segment}";
        }

        return string.IsNullOrEmpty(type.NamespaceName)
            ? segment
            : $"{type.NamespaceName}.{segment}"
            ;
    }

    /// <summary>
    /// Whether a type must be emitted by its fully qualified name. A bare name is
    /// resolved by C# against the enclosing namespace first, so a cross-project
    /// reference such as <c>Simpliest.Program</c> emitted as <c>Program</c> inside
    /// <c>namespace AllFeaturesV2;</c> silently binds to <c>AllFeaturesV2.Program</c>.
    /// </summary>
    private static bool ShouldQualify(MemberType memberType)
    {
        // The Standard namespace is handled above and maps onto C# keywords and
        // System types reached through the emitted usings.
        if(string.IsNullOrEmpty(memberType.NamespaceName)
            || memberType.NamespaceName.StartsWith("Standard.", StringComparison.Ordinal))
        {
            return false;
        }

        // Ambient types carry CLR namespaces that the emitted usings already cover, and
        // synthetic types have no declaration to qualify against. Only types declared in
        // Mist source are ours to qualify.
        if(!memberType.Assembly.HasMistSource)
        {
            return false;
        }

        // Generic definitions keep their arity-encoded name (`Array<,>`) and are
        // rendered through EmitConstructed instead.
        return !memberType.Name.Contains('<');
    }
}