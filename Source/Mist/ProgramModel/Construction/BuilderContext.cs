using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Shared compilation-wide state passed to all program model builders.
/// </summary>
internal sealed class BuilderContext
{
    public BuilderContext(
        Scope globalScope,
        Model.ProgramModel programModel,
        DiagnosticBag diagnostics,
        IAmbientTypeProvider ambientProvider)
    {
        GlobalScope = globalScope;
        ProgramModel = programModel;
        Diagnostics = diagnostics;
        AmbientProvider = ambientProvider;
        TypeResolver = new TypeReferenceResolver(this);
    }

    public Scope GlobalScope { get; }

    public Model.ProgramModel ProgramModel { get; }

    public DiagnosticBag Diagnostics { get; }

    public IAmbientTypeProvider AmbientProvider { get; }

    /// <summary>
    /// The shared type reference resolver for this compilation. Resolves AST type
    /// syntax into <see cref="Contracts.Types.IMistType"/> via scope-based lookup.
    /// </summary>
    public TypeReferenceResolver TypeResolver { get; }

    /// <summary>
    /// Extension block AST nodes collected during Phase 1.
    /// Processed in Phase 2 after all types are registered.
    /// </summary>
    public List<PendingExtension> PendingExtensions { get; } = [];

    /// <summary>
    /// With-declaration AST nodes collected during Phase 1.
    /// Processed in Phase 2c after all types and extensions are resolved.
    /// </summary>
    public List<PendingConformance> PendingConformances { get; } = [];
}