using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Builds an <see cref="IProgramModel"/> from parsed ASTs and compilation metadata.
/// This is the v2 equivalent of <c>SemanticModelBuilder</c> — it constructs
/// the fully resolved program model without referencing the legacy semantic system.
/// </summary>
public sealed class ProgramModelBuilder
{
    /// <summary>
    /// Constructs an <see cref="IProgramModel"/> from the parsed ASTs and compilation metadata.
    /// </summary>
    /// <param name="compilation">The compilation containing units with metadata.</param>
    /// <param name="asts">The syntax trees produced by Stage 3 (parsing).</param>
    /// <param name="options">Compiler options with namespace root and project folder.</param>
    /// <param name="diagnostics">Diagnostic bag to report construction errors.</param>
    /// <param name="ambientProvider">Provider for resolving CLR types from loaded assemblies.</param>
    /// <returns>The built program model, or null on fatal failure.</returns>
    public static IProgramModel? Build(
        MistCompilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        CompilerOptions options,
        DiagnosticBag diagnostics,
        IAmbientTypeProvider ambientProvider)
    {
        if(asts.Length != compilation.Units.Length)
        {
            diagnostics.Add(new Diagnostic(
                ProgramModelDiagnostics.AstUnitMismatch.Id,
                ProgramModelDiagnostics.AstUnitMismatch.DefaultSeverity,
                ProgramModelDiagnostics.AstUnitMismatch.Stage,
                string.Format(ProgramModelDiagnostics.AstUnitMismatch.MessageTemplate, asts.Length, compilation.Units.Length),
                Location: default,
                CompilationUnitPath: "<internal>",
                AnalyzerName: null));

            return null;
        }

        var programModel = new Model.ProgramModel();

        // Phase 0: Establish the compilation's declaration boundaries before any type is
        // registered, and before the context that builds scopes over them. Referenced projects are
        // read straight off the compilation, so each appears exactly once by construction rather
        // than by deduplicating a scan over the units it contributed.
        //
        // Implicit usings are supplied here and nowhere else. They belong to a project, so an
        // assembly is the only thing that can hold them without one project's choice reaching
        // another's files. The standard library declares none and has no mechanism to.
        _ = programModel.AddSelfAssembly(options.ProjectName, compilation.GlobalUsings);

        if(compilation.HasStandardLibrary)
        {
            _ = programModel.AddStandardAssembly();
        }

        foreach(var project in compilation.Projects)
        {
            _ = programModel.AddReferenceAssembly(
                project.Name, project.CanonicalTomlPath, project.GlobalUsings);
        }

        // Validate that all assembly names are unique across the compilation.
        ValidateUniqueAssemblyNames(programModel, diagnostics);

        var context = new BuilderContext(programModel, diagnostics, ambientProvider);
        var unitBuilder = new CompilationUnitBuilder(context);

        // Process each compilation unit.
        for(var i = 0; i < asts.Length; i++)
        {
            var unit = compilation.Units[i] with
            {
                Ast = asts[i],
                NamespaceRoot = compilation.Units[i].NamespaceRoot ?? options.NamespaceRoot,
                ProjectFolder = compilation.Units[i].ProjectFolder ?? options.ProjectFolder,
            };

            _ = unitBuilder.Build(unit);
        }

        // Phase 1b: Eagerly build and register all ambient types.
        // Standard Library shells are now registered, so the provider can resolve
        // well-known type aliases. All ambient types become visible for Phase 2.
        ambientProvider.BuildAndRegister(programModel);

        // Phase 2a: Resolve member signatures on all registered types.
        var concreteTypeBuilder = new ConcreteTypeBuilder(context, context.Resolver);
        var abstractTypeBuilder = new AbstractTypeBuilder(context, context.Resolver);
        var enumTypeBuilder = new EnumTypeBuilder(context, context.Resolver);
        var newtypeBuilder = new NewtypeBuilder(context, context.Resolver);
        var typeAliasBuilder = new TypeAliasBuilder(context, context.Resolver);

        var sourceTypes = programModel
            .TypesByFQN
            .Values
            .Where(t => t.Assembly.HasMistSource)
            .ToList()
            ;

        // Aliases resolve ahead of every other kind because alias expansion happens at
        // resolution (ScopeResolver.ExpandAlias): a member signature naming Optional<int>
        // needs the alias's target already in place when the signature is resolved.
        context.Resolver.AliasCompleter = typeAliasBuilder.Build;

        foreach(var typeDef in sourceTypes)
        {
            if(typeDef is TypeAlias typeAliasDef)
            {
                typeAliasBuilder.Build(typeAliasDef);
            }
        }

        foreach(var typeDef in sourceTypes)
        {
            switch(typeDef)
            {
                case ConcreteType concrete when concrete.DeclarationNode is not null:
                    concreteTypeBuilder.Build(concrete);
                    break;
                case AbstractType abstractDef:
                    abstractTypeBuilder.Build(abstractDef);
                    break;
                case EnumType enumDef:
                    enumTypeBuilder.Build(enumDef);
                    break;
                case Newtype newtypeDef:
                    newtypeBuilder.Build(newtypeDef);
                    break;
            }
        }

        // Phase 2b: Resolve extension blocks (after all types are fully resolved).
        var extensionBuilder = new ExtensionBuilder(context, context.Resolver);
        extensionBuilder.BuildAll();

        // Phase 2c: Resolve retroactive conformance declarations (with blocks).
        var conformanceBuilder = new ConformanceBuilder(context, context.Resolver);
        conformanceBuilder.BuildAll();

        // Phase 3.
        MemberBinder.BindAll(sourceTypes, context);

        return programModel;
    }

    /// <summary>
    /// Reports an error if two source-bearing assemblies share a name. Checked here rather than
    /// in a backend because it is a property of the program, not of any target: an assembly name
    /// is the handle a Mist program has on an assembly, so a duplicate is ambiguous however the
    /// program is later emitted.
    /// </summary>
    private static void ValidateUniqueAssemblyNames(Model.ProgramModel programModel, DiagnosticBag diagnostics)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach(var assembly in programModel.Assemblies)
        {
            if(!assembly.HasMistSource)
            {
                continue;
            }

            if(!seen.Add(assembly.Name))
            {
                diagnostics.Report(
                    ProgramModelDiagnostics.DuplicateAssemblyName, default, "", assembly.Name);
            }
        }
    }

}