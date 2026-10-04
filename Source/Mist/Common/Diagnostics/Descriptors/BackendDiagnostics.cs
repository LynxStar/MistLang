#pragma warning disable IDE0130 // Namespace intentionally flat so emission sites need no extra using
namespace Mist.Common.Diagnostics;
#pragma warning restore IDE0130

/// <summary>
/// Diagnostic descriptors for the C# transpiler backend, compilation driver,
/// and assembly type provider. Covers emission template errors, Roslyn compilation
/// failures, assembly load failures, and general backend exceptions.
/// </summary>
public static class BackendDiagnostics
{
    /// <summary>
    /// An intrinsic member was invoked but the backend has no emission
    /// template registered for it. The compilation must fail with no output artifact.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingIntrinsicEmissionTemplate = new(
        new DiagnosticId("MIST_BE_0002"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "No emission template found for intrinsic '{0}' in backend 'CSharp'.");

    /// <summary>
    /// No C# source files were found in the output directory for Roslyn compilation.
    /// </summary>
    public static readonly DiagnosticDescriptor BuildError = new(
        new DiagnosticId("MIST_BUILD_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "No C# source files found to compile.");

    /// <summary>
    /// A Roslyn (C#) compilation error was produced during the downstream
    /// build of emitted C# source files.
    /// </summary>
    public static readonly DiagnosticDescriptor CSharpError = new(
        new DiagnosticId("MIST_CS_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "{0}");

    /// <summary>
    /// An assembly failed to load via <c>MetadataLoadContext</c> during
    /// ambient type provider initialization.
    /// </summary>
    public static readonly DiagnosticDescriptor AssemblyLoadFailed = new(
        new DiagnosticId("MIST_ASM_LOAD_FAILED"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Failed to load assembly '{0}': {1}");

    /// <summary>
    /// A type referenced in a CLR assembly signature could not be resolved to
    /// any known ambient or Standard Library type during projection.
    /// </summary>
    public static readonly DiagnosticDescriptor UnresolvedAmbientType = new(
        new DiagnosticId("MIST_AMBIENT_UNRESOLVED"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Cannot resolve ambient type '{0}'. The assembly containing this type may not be referenced.");

    /// <summary>
    /// The emitter reached a statement the binder never resolved. Means "the compiler does not
    /// implement this yet", not "the program is wrong".
    /// <para>
    /// Reported even though the emitted comment is harmless C#, and the harmlessness is precisely why
    /// it has to be: a comment is legal where a statement was, so an unreported one leaves a program
    /// that compiles and runs with a piece of itself missing. The expression case cannot hide that
    /// way, because a comment is not an expression.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedStatement = new(
        new DiagnosticId("MIST_BE_UNSUPPORTED_STATEMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "This statement is not supported by the compiler yet, so no C# can be emitted for it.");

    /// <summary>
    /// The emitter reached an expression the binder never resolved. Means "the compiler does not
    /// implement this yet", not "the program is wrong".
    /// <para>
    /// Reported at the point of EMISSION rather than binding, because that is where an unresolved
    /// expression stops being a private matter: the binder leaves a sentinel on purpose and most
    /// sentinels are never reached, whereas writing one out produces a comment where C# needs an
    /// expression, which does not parse. Reporting here keeps the error on the Mist source instead of
    /// on a generated file the user never wrote.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedExpression = new(
        new DiagnosticId("MIST_BE_UNSUPPORTED_EXPRESSION"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "This expression is not supported by the compiler yet, so no C# can be emitted for it.");

    /// <summary>
    /// An executable was requested but the project declares no entry point. Reported by the
    /// emitter, which cannot produce a runnable artifact without one. Libraries are exempt.
    /// </summary>
    public static readonly DiagnosticDescriptor MissingEntryPoint = new(
        new DiagnosticId("MIST_MISSING_ENTRY_POINT"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Project '{0}' produces an executable but declares no entry point. Add a 'static void Main()' method, or set output type to library.");

    /// <summary>
    /// A method is declared both <c>readonly</c> and <c>ref</c>, which has two readings that differ in
    /// whether the returned alias may be written through. On a struct, <c>readonly</c> is already the
    /// modifier meaning the method does not mutate the receiver, so the pair reads either as that
    /// modifier on a method returning a writable <c>ref</c>, or as a method returning
    /// <c>ref readonly</c>.
    /// <para>
    /// Refused rather than resolved here, because picking the first silently widens a read-only alias
    /// into a writable one -- the one direction a wrong guess is unsafe -- and picking the second
    /// discards a modifier the author wrote. It is a language question: Mist writes
    /// <c>readonly ref</c> for both, and nothing in the grammar separates them. Every other position
    /// is unambiguous and does emit, since <c>readonly</c> means nothing else on a parameter or a
    /// local.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor ReadonlyRefReturn = new(
        new DiagnosticId("MIST_BE_READONLY_REF_RETURN"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Method '{0}' is declared both readonly and ref. On a struct 'readonly' already means the method does not mutate the receiver, so it is unclear whether the returned reference is itself readonly. Drop one of the two.");



    /// <summary>
    /// A COMBINATOR or a value comparison was tested against a union-typed scrutinee. A type test is
    /// fine: it narrows through the carrier accessor for its constituent, which is one scrutinee and one
    /// pattern. These are not, because each operand would need a DIFFERENT accessor and a C# pattern has
    /// only one scrutinee -- so `u is int or string` has to become two tests joined by <c>||</c>, an
    /// expression rather than a pattern, which is a second emission shape that is not built.
    /// <para>
    /// Reported rather than emitted because the C# would be rejected by Roslyn, moving the error onto a
    /// generated file the user never wrote. Optionals never reach here: they bypass the carrier for
    /// Nullable or the bare type, both of which C# tests directly.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedUnionPattern = new(
        new DiagnosticId("MIST_BE_UNSUPPORTED_UNION_PATTERN"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Only a type test can narrow the union type '{0}' yet. Combining patterns or comparing against a constant is not supported by the experimental emitter.");

    /// <summary>
    /// An enum variant appeared under a pattern combinator. A Mist enum lowers to a record class whose
    /// variants are static readonly FIELDS, and C# admits only a compile-time constant in a pattern, so
    /// the test has to become an equality comparison. That is an expression, and an expression cannot
    /// sit inside a C# pattern -- so <c>d is Direction.North or Direction.South</c> would have to become
    /// two comparisons joined by <c>||</c>, a second emission shape that is not built.
    /// <para>
    /// The bare form does emit, in both positions it can occupy: an <c>is</c> becomes the comparison
    /// outright, and a switch arm binds the scrutinee and compares in a <c>when</c> guard. Reported
    /// rather than emitted for <see cref="UnsupportedUnionPattern"/>'s reason -- Roslyn would reject the
    /// C#, moving the error onto a generated file the user never wrote.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedEnumPattern = new(
        new DiagnosticId("MIST_BE_UNSUPPORTED_ENUM_PATTERN"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "The enum variant '{0}' cannot be combined with 'and', 'or' or 'not' yet, because matching one is an equality comparison rather than a C# pattern.");

    /// <summary>
    /// A property pattern on an <c>unknown</c> scrutinee under <c>or</c> or <c>not</c>. Its members are
    /// read through a binding of the scrutinee, since C# resolves a property pattern's names against a
    /// type that an object does not have -- and neither combinator establishes a binding for the reads to
    /// go through, the same wall <see cref="UnsupportedEnumPattern"/> meets.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedUnknownPropertyPattern = new(
        new DiagnosticId("MIST_BE_UNSUPPORTED_UNKNOWN_PROPERTY_PATTERN"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "A property pattern on an 'unknown' value cannot be combined with 'or' or 'not' yet, because its members are read through a binding of the value that neither establishes.");

    /// <summary>
    /// A runtime test of a sequence whose elements carry a length of their own. Refused rather than emitted
    /// for the reason <see cref="Backends.CSharpTranspiler.Emitter.ErasedShape"/> gives: the test
    /// would check the outer length and promise the inner one.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedSizedElementTest = new(
        new DiagnosticId("MIST_BE_UNSUPPORTED_SIZED_ELEMENT_TEST"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "A value cannot be tested against '{0}' at run time yet, because its elements carry a length that only a check of every element could establish.");

    /// <summary>
    /// A struct and one of its ancestors declare a field of the same name. Legal Mist, because two
    /// levels declaring a private field cannot see each other, but the C# backend flattens
    /// inherited fields into one struct where the name can only resolve to one of them.
    /// <para>
    /// A backend that expresses struct inheritance natively has no such conflict, so this is a
    /// limitation of this backend rather than of the language.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor StructInheritanceFieldCollision = new(
        new DiagnosticId("MIST_BE_STRUCT_FIELD_COLLISION"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Field '{0}' is declared on both '{1}' and '{2}'. Inherited struct fields are flattened into the derived struct, so a name cannot be reused across the inheritance chain.");

    /// <summary>
    /// A type not declared in Mist was used to close a parameterized constructor constraint. C# has
    /// no such constraint, so the backend lowers it to a synthesized interface carrying a static
    /// abstract factory, and adds that interface to the closing type's declaration. A precompiled
    /// type has no declaration to add to, and nothing emitted can change the interface list recorded
    /// in another assembly's metadata.
    /// <para>
    /// A backend that emits its own code for the type argument -- monomorphizing, or targeting
    /// something other than the CLR -- has no such limit, so this is a limitation of THIS BACKEND
    /// rather than of the language. The parameterless <c>new()</c> form is unaffected: C# has it, so
    /// it is emitted as a real constraint and any type may close it.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor ForeignConstructorConstraintClosure = new(
        new DiagnosticId("MIST_BE_FOREIGN_CTOR_CONSTRAINT"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Type '{0}' is not declared in Mist, so it cannot close the constructor constraint 'new({1})'. The C# backend satisfies that constraint by adding an interface to the closing type, which a precompiled type cannot receive.");

    /// <summary>
    /// A method group reached emission. Unreachable by construction, since a group is not a value and
    /// there is no C# to write for one.
    /// <para>
    /// Distinct from <see cref="UnsupportedExpression"/>, which reports a construct the compiler has
    /// not built. A group has exactly two consumers -- an invocation, and a conversion to a function
    /// type -- and a site that can reach neither is required to report that and bind an error
    /// expression in its place. Arriving here therefore means the group was neither resolved nor
    /// reported, which is a fault in the compiler rather than anything the user can act on.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor MethodGroupReachedEmission = new(
        new DiagnosticId("MIST_INTERNAL_METHOD_GROUP_AT_EMISSION"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Internal compiler error: the method group '{0}' reached emission without having been resolved to a single overload. This is a bug in the compiler, not an error in your source.");

    /// <summary>
    /// A struct inherits an intrinsic struct. Flattening carries an ancestor's state into the
    /// derived struct, and an intrinsic type declares no state to carry -- its representation is the
    /// backend's, not the model's -- so the derived struct would silently drop whatever the ancestor
    /// actually holds and widen to a zeroed value.
    /// <para>
    /// Refused rather than approximated. The state could only be reached by guessing at the runtime
    /// type's private field names, which are undocumented and free to change, and the base is not
    /// ours to emit a factory onto in any case.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor IntrinsicStructBase = new(
        new DiagnosticId("MIST_BE_INTRINSIC_STRUCT_BASE"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Struct '{0}' cannot inherit intrinsic struct '{1}'. An intrinsic type's representation belongs to the backend, so its state cannot be flattened into a derived struct.");

    /// <summary>
    /// A BACKEND LIMITATION AND NOT A LANGUAGE RULE, so it is refused here rather than during
    /// construction: the model builds the conformance edge and assignability honours it, and a backend
    /// owning its runtime could record it outside the type. This one adds an interface to a
    /// declaration, so it is blocked as <see cref="ForeignConstructorConstraintClosure"/> is. Refused
    /// rather than dropped: dropping leaves every abstract-typed use of the type failing in generated
    /// C# with no Mist-level cause.
    /// </summary>
    public static readonly DiagnosticDescriptor ForeignConformance = new(
        new DiagnosticId("MIST_BE_FOREIGN_CONFORMANCE"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Type '{0}' is not declared in Mist, so it cannot be declared to conform to '{1}'. The C# backend satisfies a retroactive conformance by adding the interface to the conforming type's declaration, which a precompiled type cannot receive.");

    /// <summary>
    /// A parameterized associated type slot declares no abstract bound. C# has no higher-kinded
    /// type, so such a slot cannot be lifted the way a plain one is; the backend ERASES it to its
    /// bound instead, and an unbounded slot leaves nothing to erase to.
    /// <para>
    /// Refused rather than erased to the top type: <c>object</c> would compile and then reject every
    /// member the slot's users call, reporting against generated C#. Unlike the non-parameterized
    /// case, an unconstrained slot here is not merely unchecked but unspellable, which is why
    /// <c>type Element;</c> is fine while <c>type Element&lt;T&gt;;</c> is not.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor UnboundedParameterizedTypeSlot = new(
        new DiagnosticId("MIST_BE_UNBOUNDED_PARAMETERIZED_SLOT"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "The associated type slot '{0}.{1}' declares its own type parameters but no abstract type bound. C# has no higher-kinded types, so such a slot is emitted as its bound; give it one, or remove its type parameters.");

    /// <summary>
    /// A parameterized associated type slot types a PARAMETER. Erasure leaves the interface asking
    /// for the slot's bound while the implementor takes the nested type itself, and the two are
    /// bridged by an explicit implementation that forwards -- which a return type allows, being
    /// covariant, and a parameter does not.
    /// <para>
    /// Refused rather than bridged through a cast: the bound admits types other than the one the
    /// implementor nested, so the downcast such a forwarder needs is one the compiler cannot know
    /// will hold, and a slot exists precisely to make that static. A backend able to express
    /// higher-kinded types needs no erasure here and so has no such limit.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor ParameterizedTypeSlotInParameter = new(
        new DiagnosticId("MIST_BE_PARAMETERIZED_SLOT_PARAMETER"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Parameter '{0}' of '{1}.{2}' is typed by the parameterized associated type slot '{3}'. The experimental emitter erases such a slot to its bound, which it can bridge for a return type but not for a parameter.");

    /// <summary>
    /// A test against a refinement whose predicate did not bind, so there is nothing to test WITH.
    /// Refused rather than erased: a refinement spelled as its base type asks `x is int` of an int,
    /// which is true of every value -- so erasing here does not lose a check, it silently passes one.
    /// </summary>
    public static readonly DiagnosticDescriptor UnboundRefinementPredicate = new(
        new DiagnosticId("MIST_BE_REFINED_PREDICATE"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "The refinement '{0}' cannot be tested here: its predicate over '{1}' did not bind, and testing the base type alone would succeed for every value.");

    /// <summary>
    /// A DEPENDENT type parameter -- one whose domain is a value type rather than <c>Standard.Type</c>,
    /// as in <c>FixedArray&lt;Size:int&gt;</c>. C# has no analogue: the backend emits it as an ordinary
    /// type parameter, so <c>class FixedArray&lt;Size&gt;</c> compiles as a declaration and every body
    /// use of the VALUE then fails with CS0119, "'Size' is a type, which is not valid in the given
    /// context".
    /// <para>
    /// Refused rather than emitted because that shape is the worst of both: the declaration survives,
    /// so the failure surfaces scattered across generated bodies rather than at the parameter that
    /// caused it. The encoding that would make it work -- a per-domain witness interface with a const
    /// struct per value -- is a capability of its own, and until it exists there is nothing faithful to
    /// emit.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor DependentTypeParameter = new(
        new DiagnosticId("MIST_BE_DEPENDENT_TYPE_PARAMETER"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Type parameter '{0}' of '{1}' is dependent: it ranges over values, not types. C# has no dependent type parameter, and the experimental emitter does not yet encode one.");

    /// <summary>
    /// A pack survived into a signature that no specialization binds, so there is no width to write
    /// it at. Reported rather than emitted because the alternative is C# naming a type parameter
    /// that does not exist there -- valid-looking output that does not compile, in a file the user
    /// never wrote.
    /// </summary>
    public static readonly DiagnosticDescriptor UnhoistedUnitCall = new(
        new DiagnosticId("MIST_BE_UNHOISTED_UNIT_CALL"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "'{0}' yields nothing and its value is taken {1}. The experimental emitter supplies that value by running the call as a statement, and has nowhere to put one there.");

    public static readonly DiagnosticDescriptor UnspecializedPack = new(
        new DiagnosticId("MIST_BE_UNSPECIALIZED_PACK"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "'{0}' mentions the pack '{1}' where no arity is fixed, so no C# signature can be written for it.");

    /// <summary>
    /// A union over one element of a pack, or a construction that holds one, where the pack's declaration
    /// is one <c>UnionSpecialization</c> cannot emit once per instantiation: a type and the methods of a
    /// type are, so what remains is an extension block's method. Written per arity the union loses its
    /// optional for a value type.
    /// </summary>
    public static readonly DiagnosticDescriptor UnionOverPackElement = new(
        new DiagnosticId("MIST_BE_UNION_OVER_PACK_ELEMENT"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "'{0}' is spelled differently in C# for each type argument, and it names '{1}', an element of a pack declared where the C# backend cannot yet emit a declaration once per instantiation, as it does a type and a type's methods. An optional becomes spellable when '{2}' is constrained with 'class' or 'struct'.");

    public static readonly DiagnosticDescriptor UnboundedSpecialization = new(
        new DiagnosticId("MIST_BE_UNBOUNDED_SPECIALIZATION"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "'{0}' writes a union over its own type parameters, so it is emitted once per instantiation, and its instantiations keep producing new ones past {1}: an instantiation that needs a larger one of itself never finishes.");

    public static readonly DiagnosticDescriptor DelegateCeilingExceeded = new(
        new DiagnosticId("MIST_VAR_0010"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "{0} takes {1} inputs, and System.Func and System.Action hold at most {2}. The limit is the C# backend's, which has no delegate to emit this function as, and not the language's.");
}