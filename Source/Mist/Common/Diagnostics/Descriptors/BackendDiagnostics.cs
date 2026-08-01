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
    /// A derived-to-base struct conversion cannot copy one of the base's fields, because the field
    /// is private to the base or is readonly. Emitting the conversion anyway would produce a base
    /// value missing part of its state.
    /// </summary>
    public static readonly DiagnosticDescriptor StructConversionFieldInaccessible = new(
        new DiagnosticId("MIST_BE_STRUCT_CONVERSION_FIELD"),
        DiagnosticSeverity.Error,
        PipelineStage.Backend,
        "Cannot emit the conversion from '{1}' to '{0}' because field '{2}' is private or readonly and cannot be copied.");
}