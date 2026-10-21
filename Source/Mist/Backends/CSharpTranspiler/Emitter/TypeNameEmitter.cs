using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
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
    private static Frame? substituting;

    [ThreadStatic]
    private static bool keying;

    [ThreadStatic]
    private static bool oblivious;

    /// <summary>
    /// Spells every type parameter bound by <paramref name="substitution"/> as its argument for the
    /// scope's duration: how a member inherited from a generic base is written inside the struct that
    /// absorbed it, where the base's parameters are not in scope and its arguments are.
    /// </summary>
    public static IDisposable Substituting(TypeSubstitution substitution)
    {
        var enclosing = substituting;
        substituting = new Frame(substitution, enclosing);
        return new SubstitutionScope(enclosing);
    }

    public static IDisposable Oblivious()
    {
        var outer = oblivious;
        oblivious = true;
        return new ObliviousScope(outer);
    }

    internal static bool IsOblivious()
    {
        return oblivious;
    }

    /// <summary>
    /// A type as the enclosing scopes close it, innermost first: a generic method emitted inside a
    /// specialized type binds its own parameters and leaves the type's to the scope around it.
    /// </summary>
    public static IMistType Closed(IMistType type)
    {
        for(var frame = substituting; frame is not null; frame = frame.Outer)
        {
            type = frame.Substitution.Apply(type, interner: null);
        }

        return type;
    }

    internal static string? SpecializedCallName(
        IReadOnlyList<IMistType> typeArguments, IReadOnlyList<TypeParameter>? parameters)
    {
        if(UnionSpecialization.MethodDeclaring(parameters) is not { } method)
        {
            return null;
        }

        var closed = typeArguments.Select(Closed).ToList();

        return UnionSpecialization.IsClosed(closed) ? UnionSpecialization.Name(method.Name, closed) : null;
    }

    public static string Emit(IMistType type)
    {
        type = Closed(type);

        return type switch
        {
            ConstructedType constructed => EmitConstructed(constructed),
            LiftedType lifted => Emit(lifted.Type),
            UnknownValueType unknownValue => Emit(unknownValue.Domain),
            UnionType union => EmitUnion(union),
            NamedTupleType named => EmitTuple(named.ElementTypes, keying ? null : named.ElementNames),
            TupleType tuple => EmitTuple(tuple.ElementTypes, elementNames: null),
            FunctionType function => EmitFunction(function),
            MemberType memberType => MapMemberType(memberType),
            EnumType enumType => ShouldQualify(enumType) ? Qualified(enumType) : IdentifierSpelling.Of(enumType.Name),
            PackElementType element => Emit(Specialized(element)),
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
    /// An index past the width the specialization fixes is reachable only in code that width rules out --
    /// a tuple pattern or a narrowing guard spelling another width -- and that code must still compile, so
    /// it reads as <c>object</c>, which can hold anything it is never handed.
    /// </summary>
    private static IMistType Specialized(PackElementType element)
    {
        var monomorph = Monomorph.InScope();

        return monomorph?.Element(element.Pack, element.Index)
            ?? (monomorph?.Binds(element.Pack) == true ? UnknownType.Instance : element.Pack);
    }

    /// <summary>
    /// The runtime key an ambient value is filed under. A context is found by EXACT type, and erasing
    /// a pure function type's promise would file a plain function under the key a <c>with</c> clause
    /// requiring a pure one reads -- so a pure function type, at any depth, keys as its own marker.
    /// Optional reference keys also carry a marker because the CLR spelling of an optional reference
    /// is identical to its required form.
    /// </summary>
    public static string EmitContextKey(IMistType type)
    {
        var outer = keying;
        keying = true;

        try
        {
            return Emit(type);
        }
        finally
        {
            keying = outer;
        }
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
        type = Closed(type);

        if(type is TypeAlias { AliasedType: { } aliased })
        {
            return EmitAsPatternType(aliased);
        }

        if(type is RefinedType refined)
        {
            return EmitAsPatternType(refined.BaseType);
        }

        var elementTypes = type switch
        {
            NamedTupleType named => named.Underlying.ElementTypes,
            TupleType tuple => tuple.ElementTypes,
            _ => null,
        };

        return elementTypes is null
            ? EmitUnannotated(type)
            : ValueTupleNesting.Spelled([.. Expand(elementTypes).Select(Emit)])
            ;
    }

    /// <summary>
    /// The C# spelling of "a T, or absent": Nullable of T for a value kind, and for a reference kind the
    /// type annotated nullable, null standing for absence. The annotation is the generated project's
    /// nullable context reading the slot as the optional it is; bare, an absent field is CS8618. A kind
    /// that is neither -- an unconstrained type parameter -- stays bare, C# having no spelling of "a T, or
    /// absent" for it at all.
    /// </summary>
    internal static string EmitOptional(IMistType type)
    {
        var name = Emit(type);
        var valueKind = keying ? IsContextValueKind(type) : IsValueKind(type);

        if(keying && !valueKind)
        {
            return $"MistContext.Optional<{name}>";
        }

        return valueKind || (!oblivious && IsReferenceKind(type)) ? $"{name}?" : name;
    }

    /// <summary>
    /// The spelling where C# refuses a nullable reference type outright -- <c>typeof</c>, <c>as</c> and a
    /// pattern's type -- which differs from <see cref="Emit"/> only for an optional reference, spelled bare.
    /// </summary>
    internal static string EmitUnannotated(IMistType type)
    {
        return Unaliased(Closed(type)) is UnionType union && OptionalUnion.InnerType(union) is { } inner && !IsValueKind(inner)
            ? Emit(inner)
            : Emit(type)
            ;
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

    private static IMistType Unaliased(IMistType type)
    {
        return type is TypeAlias { AliasedType: { } aliased } ? Unaliased(aliased) : type;
    }

    private static bool IsContextValueKind(IMistType type)
    {
        return type switch
        {
            TypeAlias { AliasedType: { } aliased } => IsContextValueKind(aliased),
            RefinedType refined => IsContextValueKind(refined.BaseType),
            _ => IsValueKind(type),
        };
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
            TypeAlias { AliasedType: { } aliased } => IsValueKind(aliased),
            ConstructedType constructed => IsValueKind(constructed.Definition),
            TypeParameter parameter => HasStructConstraint(parameter),
            _ => false,
        };
    }

    internal static bool IsReferenceKind(IMistType type)
    {
        return type switch
        {
            ClassType or AbstractType or FunctionType or UnknownType => true,
            RefinedType refined => IsReferenceKind(refined.BaseType),
            TypeAlias { AliasedType: { } aliased } => IsReferenceKind(aliased),
            ConstructedType constructed => IsReferenceKind(constructed.Definition),
            TypeParameter parameter => parameter.ConstraintsList.Exists(
                constraint => constraint is KeywordConstraint { Kind: TypeParameterConstraintKind.Class }),
            _ => false,
        };
    }

    // A value-type KEYWORD, never a type constraint that happens to name a struct: C# reads
    // `K?` as Nullable of K only under that guarantee, and spells the same `?` as a reference
    // annotation otherwise -- so a parameter constrained to a struct BY TYPE would take this
    // spelling and not the meaning. An unconstrained parameter has no C# type for "a K, or absent"
    // at all and is the case a carrier would be for.
    private static bool HasStructConstraint(TypeParameter parameter)
    {
        return parameter.ConstraintsList.Exists(
            constraint => constraint is KeywordConstraint { Kind: TypeParameterConstraintKind.Struct or TypeParameterConstraintKind.Stampable });
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
        var spelled = EmitDelegate(function);

        return keying && function.IsPure ? $"MistContext.Pure<{spelled}>" : spelled;
    }

    private static string EmitDelegate(FunctionType function)
    {
        var parameters = Expand(function.ParameterTypes).Select(Emit).ToList();
        DelegateCeiling.Spelled(function, parameters.Count);

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
                ? $"{spelled} {IdentifierSpelling.Of(name)}"
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
    internal static IReadOnlyList<IMistType> Expand(IReadOnlyList<IMistType> elementTypes)
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

        if(constructed.Definition is ConcreteType { IsIntrinsic: true } intrinsic && ClrSpelling.Spelling(intrinsic) is { } substituted)
        {
            return $"{substituted}<{string.Join(", ", constructed.TypeArguments.Select(Emit))}>";
        }

        if(UnionSpecialization.NameOf(constructed) is { } specializedName)
        {
            return Qualify(constructed.Definition, specializedName);
        }

        // A variadic construction names the SPECIALIZATION its arity asks for, with the pack
        // spliced into the argument list: C# has no pack, so `Box<[int, string]>` is
        // `Box_2<int, string>` and the declaration it names was emitted for that width alone --
        // unless the family is emitted per instantiation, asked first, which has no per-arity one.
        if(MonomorphReference.Emit(constructed, Emit) is { } specialized)
        {
            return specialized;
        }

        // Generic types: Definition<Arg1, Arg2, ...>
        var baseName = IdentifierSpelling.Of(MistType.StripArity(constructed.Definition.Name));
        var args = string.Join(", ", constructed.TypeArguments.Select(
            (argument, ordinal) => EmitArgument(constructed.Definition, ordinal, argument)));

        return Qualify(constructed.Definition, $"{baseName}<{args}>");
    }

    private static string Qualify(IMistType definition, string name)
    {
        if(definition is MemberType { DeclaringType: MemberType declaring })
        {
            return $"{Qualified(declaring)}.{name}";
        }

        // Qualified for the reason ShouldQualify gives.
        return definition is DeclaredType { NamespaceName: { Length: > 0 } namespaceName }
            ? $"{IdentifierSpelling.Qualified(namespaceName)}.{name}"
            : name
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

        var segment = IdentifierSpelling.Of(MistType.StripArity(nested.Definition.Name));

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
        if(ClrSpelling.Spelling(memberType) is { } substituted)
        {
            return substituted;
        }

        return ShouldQualify(memberType) ? Qualified(memberType) : IdentifierSpelling.Of(memberType.Name);
    }

    /// <summary>
    /// Built segment by segment because <see cref="IMistType.FullyQualifiedName"/> keeps the arity
    /// marker a generic segment is registered under, leaving a nesting path that is not a C# name.
    /// </summary>
    private static string Qualified(DeclaredType type)
    {
        var segment = type is MemberType { TypeParametersList.Count: > 0 } generic
            ? $"{IdentifierSpelling.Of(MistType.StripArity(generic.Name))}<{string.Join(", ", generic.TypeParametersList.Select(p => IdentifierSpelling.Of(p.Name)))}>"
            : IdentifierSpelling.Of(type.Name);

        if(type.DeclaringType is DeclaredType declaring)
        {
            return $"{Qualified(declaring)}.{segment}";
        }

        return string.IsNullOrEmpty(type.NamespaceName)
            ? segment
            : $"{IdentifierSpelling.Qualified(type.NamespaceName)}.{segment}"
            ;
    }

    /// <summary>
    /// Whether a type must be emitted by its fully qualified name. A bare name is
    /// resolved by C# against the enclosing namespace first, so a cross-project
    /// reference such as <c>Simpliest.Program</c> emitted as <c>Program</c> inside
    /// <c>namespace AllFeaturesV2;</c> silently binds to <c>AllFeaturesV2.Program</c>.
    /// </summary>
    private static bool ShouldQualify(DeclaredType type)
    {
        if(string.IsNullOrEmpty(type.NamespaceName))
        {
            return false;
        }

        // Synthetic types have no declaration to qualify against. An AMBIENT type is qualified like
        // any other, since EmitUsings writes only what the source imported.
        if(!type.Assembly.HasMistSource && type.Assembly.Kind != MistAssemblyKind.Ambient)
        {
            return false;
        }

        // A generic definition is named through its construction, in EmitConstructed; a bare one
        // keeps its arity marker, having no C# name.
        return !type.Name.Contains('<');
    }

    private sealed record Frame(TypeSubstitution Substitution, Frame? Outer);

    private sealed class SubstitutionScope(Frame? enclosing) : IDisposable
    {
        public void Dispose()
        {
            substituting = enclosing;
        }
    }

    private sealed class ObliviousScope(bool outer) : IDisposable
    {
        public void Dispose()
        {
            oblivious = outer;
        }
    }
}