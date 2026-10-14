using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding.StorageWrites;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Shared compilation-wide state passed to all program model builders.
/// </summary>
internal sealed class BuilderContext
{
    private readonly ProgramScope programScope;
    private readonly Dictionary<MistAssembly, AssemblyScope> assemblyScopes = [];

    public BuilderContext(
        Model.ProgramModel programModel,
        DiagnosticBag diagnostics,
        IAmbientTypeProvider ambientProvider)
    {
        ProgramModel = programModel;
        Diagnostics = diagnostics;
        AmbientProvider = ambientProvider;
        programScope = new ProgramScope(programModel);
        Resolver = new ScopeResolver(this);
        Domains = new TypeParameterDomainResolver(this, Resolver);
        Attributes = new AttributeBuilder(this);
    }

    /// <summary>
    /// The level holding an assembly's implicit usings, and the parent of every file scope belonging
    /// to it. Interned per assembly so that two files of one project share a level, which is what
    /// makes an ambiguity between two implicit usings detectable at all.
    /// <para>
    /// The <see cref="ProgramScope"/> above them is not exposed: nothing outside the chain needs it,
    /// because the queries that are not chain walks go to <see cref="ProgramModel"/> instead.
    /// </para>
    /// </summary>
    public AssemblyScope ScopeFor(MistAssembly assembly)
    {
        if(!assemblyScopes.TryGetValue(assembly, out var scope))
        {
            scope = new AssemblyScope(programScope, assembly, ProgramModel, Diagnostics);
            assemblyScopes[assembly] = scope;
        }

        return scope;
    }

    public TypeParameterDomainResolver Domains { get; }

    public Model.ProgramModel ProgramModel { get; }

    public DiagnosticBag Diagnostics { get; }

    public IAmbientTypeProvider AmbientProvider { get; }

    /// <summary>
    /// The shared scope resolver for this compilation. Resolves names and AST type
    /// syntax into types, variables, or other symbols via scope-based lookup.
    /// </summary>
    public ScopeResolver Resolver { get; }

    public AttributeBuilder Attributes { get; }

    public StorageWriteSummaries StorageWrites { get; } = new();

    /// <summary>
    /// Using directives collected during Phase 1, judged by <see cref="UsingValidation"/> once every
    /// namespace they could name is registered.
    /// </summary>
    public List<PendingUsing> PendingUsings { get; } = [];

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