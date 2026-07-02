using Mist.Common.Diagnostics;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Identifies which kind of inheritance relationship was omitted because the referenced
/// CLR type could not be resolved from any assembly available to the provider
/// (Requirement 6.6).
/// </summary>
internal enum InheritanceRelationshipKind
{
    /// <summary>The omitted relationship was a base-type (<c>extends</c>) reference.</summary>
    BaseType,

    /// <summary>The omitted relationship was an implemented-interface reference.</summary>
    Abstract,
}

/// <summary>
/// A diagnostic indication that a CLR base type or implemented interface could not be
/// resolved during projection, so the relationship was omitted from the projected derived
/// type while the derived type and its remaining members/relationships continued to project
/// (Requirement 6.6).
///
/// <para>
/// This is the structured record the <see cref="TypeProjector"/> collects and the
/// <see cref="AssemblyTypeProvider"/> surfaces. It identifies <em>which</em> relationship was
/// dropped (base type vs interface), the derived type whose relationship was dropped, and the
/// fully-qualified name of the type that could not be resolved.
/// </para>
/// </summary>
internal readonly record struct UnresolvedRelationship(
    InheritanceRelationshipKind Kind,
    string DerivedTypeName,
    string UnresolvedTypeName)
{
    /// <summary>
    /// Builds a human-readable description of the omitted relationship for surfacing as a
    /// diagnostic indication.
    /// </summary>
    public string Describe()
    {
        return Kind switch
        {
            InheritanceRelationshipKind.BaseType =>
                $"Base type '{UnresolvedTypeName}' of '{DerivedTypeName}' could not be resolved from any "
                + "available assembly; the base-type relationship was omitted from projection.",
            _ =>
                $"Implemented interface '{UnresolvedTypeName}' of '{DerivedTypeName}' could not be resolved "
                + "from any available assembly; the interface relationship was omitted from projection.",
        };
    }

    /// <summary>
    /// Materializes this indication as a non-fatal <see cref="DiagnosticSeverity.Info"/>
    /// <see cref="Diagnostic"/> with the stable identifier
    /// <c>MIST_INTEROP_UNRESOLVED_RELATIONSHIP</c>. Projection is not aborted by this
    /// indication, so the severity is informational.
    /// </summary>
    public Diagnostic ToDiagnostic()
    {
        return new(
        CommonDiagnostics.CrossAssemblyRelationshipUnresolved.Id,
        DiagnosticSeverity.Info,
        PipelineStage.Driver,
        Describe(),
        Location: default,
        CompilationUnitPath: "<assembly>",
        AnalyzerName: null);
    }
}