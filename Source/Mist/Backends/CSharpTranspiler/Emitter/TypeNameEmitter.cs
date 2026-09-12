using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
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
    /// <para>
    /// A <see cref="RefinedType"/> is spelled as the type it refines, its predicate being a
    /// compile-time constraint on which values may reach the slot rather than a different runtime
    /// representation. Where the predicate has to be TESTED rather than assumed -- an `is` against a
    /// refinement -- the test carries it, and erasing there would make the test succeed for every
    /// value; see <c>BodyEmitter.EmitPattern</c>.
    /// </para>
    /// </summary>
    [ThreadStatic]
    private static TypeSubstitution? substituting;

    /// <summary>
    /// Spells every type parameter bound by <paramref name="substitution"/> as its argument for the
    /// scope's duration: how a member inherited from a generic base is written inside the struct that
    /// absorbed it, where the base's parameters are not in scope and its arguments are.
    /// </summary>
    public static IDisposable Substituting(TypeSubstitution substitution)
    {
        var enclosing = substituting;
        substituting = substitution;
        return new SubstitutionScope(enclosing);
    }

    public static string Emit(IMistType type)
    {
        if(substituting is { } active)
        {
            type = active.Apply(type, interner: null);
        }

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
            PackElementType element => Emit(
                Monomorph.InScope()?.Element(element.Pack, element.Index) ?? element.Pack),
            TypeParameter typeParameter => DependentCarrierNames.Parameter(typeParameter),
            RefinedType refined => Emit(refined.BaseType),
            TypeAlias { AliasedType: { } aliased } => Emit(aliased),
            ErrorType => "/* error */",
            AnonymousType => "/* anonymous */",
            UnknownType => "object",
            _ => type.Name,
        };
    }

    /// <summary>
    /// The spelling for a variable whose type C# can infer from its initializer -- a local, a foreach
    /// variable, a deconstruction target. <c>var</c> wherever the type mentions an anonymous type, which
    /// C# has no name for at all, and the ordinary spelling everywhere else so the emitted declaration
    /// still states what the MODEL resolved rather than leaving C# to reach its own answer.
    /// <para>
    /// One hook for the three positions, because a check repeated at each is one that gets missed at the
    /// next -- and the failure it would let through is the worst kind, a generated file that does not
    /// parse.
    /// </para>
    /// </summary>
    internal static string EmitInferable(IMistType type)
    {
        return AnonymousReach.Mentions(type) ? "var" : Emit(type);
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
        var parameters = Expand(function.ParameterTypes).Select(Emit).ToList();

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

    /// <summary>
    /// A tuple, with any spread among its elements expanded to the width the specialization being
    /// emitted fixed.
    /// <para>
    /// C#'s tuple SYNTAX cannot spell every width -- it needs at least two elements -- so the one
    /// and zero cases name ValueTuple outright. They are reachable: a value pack over a
    /// single-element or empty pack is a real type, and `()` and `(T)` are not.
    /// </para>
    /// </summary>
    private static string EmitTuple(IReadOnlyList<IMistType> elementTypes, IReadOnlyList<string?>? elementNames)
    {
        var expanded = Expand(elementTypes);
        var named = ReferenceEquals(expanded, elementTypes) ? elementNames : null;
        var parts = new string[expanded.Count];

        for(var i = 0; i < expanded.Count; i++)
        {
            var spelled = Emit(expanded[i]);

            parts[i] = named is not null && i < named.Count && named[i] is { } name
                ? $"{spelled} {name}"
                : spelled
                ;
        }

        return parts.Length switch
        {
            0 => "System.ValueTuple",
            1 => $"System.ValueTuple<{parts[0]}>",
            _ => $"({string.Join(", ", parts)})",
        };
    }

    /// <summary>
    /// The element list with a spread replaced by what the specialization bound it to, or the list
    /// unchanged when it holds none.
    /// </summary>
    private static IReadOnlyList<IMistType> Expand(IReadOnlyList<IMistType> elementTypes)
    {
        if(Monomorph.InScope() is not { } monomorph || !elementTypes.Any(e => e is SpreadType))
        {
            return elementTypes;
        }

        var expanded = new List<IMistType>(elementTypes.Count);

        foreach(var element in elementTypes)
        {
            if(element is SpreadType spread
                && spread.Operand is TypeParameter operand
                && monomorph.Binds(operand))
            {
                expanded.AddRange(monomorph.Expansion(operand));
                continue;
            }

            expanded.Add(element);
        }

        return expanded;
    }

    private static string EmitConstructed(ConstructedType constructed)
    {
        if(SlotLift.Erased(constructed) is { } erased)
        {
            return Emit(erased);
        }

        if(constructed.Enclosing is { } enclosing)
        {
            return EmitNested(constructed, enclosing);
        }

        if(ClrProjection.Name(constructed.Definition) is { } projected)
        {
            return $"{projected}<{string.Join(", ", constructed.TypeArguments.Select(Emit))}>";
        }

        if(LengthErasure.Emit(constructed, Emit) is { } lengthIndexed)
        {
            return lengthIndexed;
        }

        if(TaskProjection.Emit(constructed, Emit) is { } task)
        {
            return task;
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

        // A variadic construction names the SPECIALIZATION its arity asks for, with the pack
        // spliced into the argument list: C# has no pack, so `Box<[int, string]>` is
        // `Box_2<int, string>` and the declaration it names was emitted for that width alone.
        if(MonomorphReference.Emit(constructed, Emit) is { } specialized)
        {
            return specialized;
        }

        // Generic types: Definition<Arg1, Arg2, ...>
        var baseName = MistType.StripArity(constructed.Definition.Name);
        var args = string.Join(", ", constructed.TypeArguments.Select(
            (argument, ordinal) => EmitArgument(constructed.Definition, ordinal, argument)));

        if(constructed.Definition is MemberType { DeclaringType: MemberType declaring })
        {
            return $"{Qualified(declaring)}.{baseName}<{args}>";
        }

        // A NAMESPACE-LEVEL generic is qualified like every other type name here. The emitted file
        // imports nothing but System, so a bare name resolves only where the reference happens to sit
        // in the same namespace -- which Standard.IClonable, named from a sample's own namespace,
        // never does.
        return constructed.Definition is DeclaredType { NamespaceName: { Length: > 0 } namespaceName }
            ? $"{namespaceName}.{baseName}<{args}>"
            : $"{baseName}<{args}>"
            ;
    }

    /// <summary>
    /// <c>Box&lt;int&gt;.Wrapper</c>, spelled as its enclosing instantiation and then its own segment,
    /// which is how C# names a nested type of a constructed generic. An enclosing that spells
    /// <c>dynamic</c> has no members to name, so the nested type is <c>dynamic</c> with it.
    /// </summary>
    private static string EmitNested(ConstructedType nested, ConstructedType enclosing)
    {
        if(ArrayEnumeration.Emit(nested, Emit) is { } enumerator)
        {
            return enumerator;
        }

        var outer = Emit(enclosing);

        if(outer == "dynamic")
        {
            return outer;
        }

        var segment = MistType.StripArity(nested.Definition.Name);

        if(nested.TypeArguments.Count == 0)
        {
            return $"{outer}.{segment}";
        }

        var args = string.Join(", ", nested.TypeArguments.Select(
            (argument, ordinal) => EmitArgument(nested.Definition, ordinal, argument)));

        return $"{outer}.{segment}<{args}>";
    }

    /// <summary>
    /// A type argument, which is the one position where a lifted value is spelled as its CARRIER
    /// rather than as its domain. Everywhere else a lifted type is an expression's type -- an
    /// <c>arr.Length</c> read -- and the domain is the only faithful projection; here the value is
    /// what the construction is identified BY, so erasing it to the domain would make
    /// FixedBuffer&lt;5&gt; and FixedBuffer&lt;6&gt; one C# type. Array is handled above and never
    /// reaches this.
    /// </summary>
    private static string EmitArgument(IMistType definition, int ordinal, IMistType argument)
    {
        if(argument is not LiftedType lifted
            || definition is not IHasTypeParameters generic
            || ordinal >= generic.TypeParametersList.Count
            || generic.TypeParametersList[ordinal].Domain is not { } domain)
        {
            return Emit(argument);
        }

        return DependentCarrierNames.Carrier(domain, lifted) ?? Emit(argument);
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
                "Regex" => RegexLowering.ClrType,

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

        // Synthetic types have no declaration to qualify against. An AMBIENT type is qualified
        // like any other: the emitted usings cover only what the Mist source imported, and a
        // reference written out in full -- `Newtonsoft.Json.JsonConvert` -- imports nothing.
        if(!memberType.Assembly.HasMistSource && memberType.Assembly.Kind != MistAssemblyKind.Ambient)
        {
            return false;
        }

        // Generic definitions keep their arity-encoded name (`Array<,>`) and are
        // rendered through EmitConstructed instead.
        return !memberType.Name.Contains('<');
    }

    private sealed class SubstitutionScope(TypeSubstitution? enclosing) : IDisposable
    {
        public void Dispose()
        {
            substituting = enclosing;
        }
    }
}