using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Shared compilation-wide state passed to all program model builders.
/// </summary>
internal sealed class BuilderContext
{
    public BuilderContext(
        GlobalScope globalScope,
        Model.ProgramModel programModel,
        DiagnosticBag diagnostics,
        IAmbientTypeProvider ambientProvider)
    {
        GlobalScope = globalScope;
        ProgramModel = programModel;
        Diagnostics = diagnostics;
        AmbientProvider = ambientProvider;
        Resolver = new ScopeResolver(this);
    }

    /// <summary>
    /// The level holding the project's implicit usings, and the parent of every file scope in the
    /// compilation. The <see cref="ProgramScope"/> above it is not exposed: nothing outside the
    /// chain needs it, because the queries that are not chain walks go to
    /// <see cref="ProgramModel"/> instead.
    /// </summary>
    public GlobalScope GlobalScope { get; }

    public Model.ProgramModel ProgramModel { get; }

    public DiagnosticBag Diagnostics { get; }

    public IAmbientTypeProvider AmbientProvider { get; }

    /// <summary>
    /// The shared scope resolver for this compilation. Resolves names and AST type
    /// syntax into types, variables, or other symbols via scope-based lookup.
    /// </summary>
    public ScopeResolver Resolver { get; }

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