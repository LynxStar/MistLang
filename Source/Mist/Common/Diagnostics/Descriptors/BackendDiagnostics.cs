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
    /// A method implicitly overrides a base class method and was emitted with
    /// the <c>override</c> modifier. Informational notice to the developer.
    /// </summary>
    public static readonly DiagnosticDescriptor ImplicitOverrideInfo = new(
        new DiagnosticId("MIST_BE_INFO_OVERRIDE"),
        DiagnosticSeverity.Info,
        PipelineStage.Backend,
        "Method '{0}' implicitly overrides '{1}' (emitted as '{2} override'). If this was not intentional, rename the method.");

    /// <summary>
    /// An unhandled exception occurred in the C# transpiler backend.
    /// The compilation cannot produce an output artifact.
    /// </summary>
    public static readonly DiagnosticDescriptor BackendError = new(
        new DiagnosticId("MIST_BACKEND_ERROR"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "C# transpiler backend failed: {0}");

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
    /// The same type (namespace + name) was found in multiple assemblies without
    /// type forwarding. This typically indicates conflicting NuGet packages.
    /// </summary>
    public static readonly DiagnosticDescriptor DuplicateAmbientType = new(
        new DiagnosticId("MIST_AMBIENT_DUPLICATE"),
        DiagnosticSeverity.Error,
        PipelineStage.Driver,
        "Type '{0}.{1}' is defined in multiple assemblies ('{2}' and others). Remove the conflicting package.");

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
    /// A parameter is declared <c>ref</c>, which the v2 emitter does not yet support.
    /// Without this diagnostic the parameter would be emitted as pass-by-value, producing
    /// a program that compiles and runs but silently computes wrong results.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedRefParameter = new(
        new DiagnosticId("MIST_BE_UNSUPPORTED_REF"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Parameter '{0}' is declared ref, which is not yet supported by the experimental emitter.");

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
    /// A switch expression whose scrutinee is a union. Narrowing a union folds the tag check into the
    /// SCRUTINEE -- <c>u is int i</c> emits as <c>u.AsInt32OrNull is int i</c> -- and a C# switch has one
    /// scrutinee for all of its arms, while each arm here needs a different constituent accessor. So the
    /// switch has to become a chain of tests rather than a switch at all, which is a second emission
    /// shape that is not built.
    /// <para>
    /// Distinct from <see cref="UnsupportedUnionPattern"/>, which refuses a PATTERN that cannot narrow:
    /// every arm below may be individually narrowable and the construct still has no lowering. Reported
    /// rather than emitted because the C# compiles nowhere -- a carrier stands in no relation to its
    /// constituents, so Roslyn rejects each arm against a generated file the user never wrote.
    /// </para>
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedUnionSwitch = new(
        new DiagnosticId("MIST_BE_UNSUPPORTED_UNION_SWITCH"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "A switch expression cannot narrow the union type '{0}' yet, because each arm would need its own constituent accessor and a C# switch admits only one scrutinee. Use a chain of 'is' tests instead.");

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
    /// An analyzer's judgement under the test
    /// <see cref="ProgramModelDiagnostics.ConformanceSourceNotConcrete"/> records, reported at
    /// emission because Stage 5 is an empty stub and because there is no valid C# to write
    /// instead -- the interface ends up named twice, or the slot implemented twice. Refused
    /// rather than collapsed: deduplicating the emitted base list runs a program the language
    /// forbids, and where two blocks map one slot to different members it also picks a winner on
    /// the author's behalf.
    /// </summary>
    public static readonly DiagnosticDescriptor RedundantConformance = new(
        new DiagnosticId("MIST_BE_REDUNDANT_CONFORMANCE"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "'{0}' is already declared to conform to '{1}', so this with block is redundant. A type reaches an abstract once: remove the with block, or remove '{1}' from the base list, or drop the duplicate with block.");
}