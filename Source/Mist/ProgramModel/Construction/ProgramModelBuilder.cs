using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Scaffolding;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model.Scopes;
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
        var globalScope = CreateGlobalScope(programModel, diagnostics, options);
        var context = new BuilderContext(globalScope, programModel, diagnostics, ambientProvider);
        var unitBuilder = new CompilationUnitBuilder(context);

        // Phase 0: Establish the compilation's declaration boundaries before any type is
        // registered. Referenced projects are read straight off the compilation, so each
        // appears exactly once by construction rather than by deduplicating a scan over
        // the units it contributed.
        _ = programModel.AddSelfAssembly(options.ProjectName);

        if(compilation.HasStandardLibrary)
        {
            _ = programModel.AddStandardAssembly();
        }

        foreach(var project in compilation.Projects)
        {
            _ = programModel.AddReferenceAssembly(project.Name, project.CanonicalTomlPath);
        }

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

        foreach(var typeDef in sourceTypes)
        {
            switch(typeDef)
            {
                case ConcreteType concrete:
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
                case TypeAlias typeAliasDef:
                    typeAliasBuilder.Build(typeAliasDef);
                    break;
            }
        }

        // Phase 2b: Resolve extension blocks (after all types are fully resolved).
        var extensionBuilder = new ExtensionBuilder(context, context.Resolver);
        extensionBuilder.BuildAll();

        // Phase 2c: Resolve retroactive conformance declarations (with blocks).
        var conformanceBuilder = new ConformanceBuilder(context, context.Resolver);
        conformanceBuilder.BuildAll();

        // Phase 3: Bind member bodies into the resolved, AST-free bound tree.
        // Walking-skeleton scope: delegate bodies and field/property initializers — property
        // and indexer ACCESSOR bodies are not yet bound. Constructs the binder does not yet
        // handle bind to sentinel nodes (no throws, no diagnostics), so this is safe to run
        // over every source program. Order is irrelevant: Phase 2 resolved every signature
        // already, so no body, chain or initializer depends on which member was bound first.
        foreach(var typeDef in sourceTypes)
        {
            if(typeDef is not MemberType memberType)
            {
                continue;
            }

            foreach(var boundDelegate in memberType.EnumerateDelegates())
            {
                // Abstract members and intrinsics legitimately have no body to bind.
                if(boundDelegate.Body is not null)
                {
                    BodyBinder.BindDelegate(boundDelegate, context);
                }
            }

            BindInitializers(memberType, context);
        }

        return programModel;
    }

    /// <summary>
    /// An initializer is an expression rather than a body, so <c>EnumerateDelegates</c> does not
    /// reach it and the delegate walk cannot bind it.
    /// </summary>
    private static void BindInitializers(MemberType memberType, BuilderContext context)
    {
        if(memberType is ConcreteType concreteType)
        {
            foreach(var field in concreteType.FieldsList)
            {
                field.Initializer = BodyBinder.BindInitializer(
                    field.Initializer, field.Type, memberType.Scope, context);
            }
        }

        foreach(var property in memberType.PropertiesList)
        {
            property.Initializer = BodyBinder.BindInitializer(
                property.Initializer, property.Type, memberType.Scope, context);
        }
    }

    /// <summary>
    /// Builds the root of every scope chain in the compilation: the <see cref="ProgramScope"/>
    /// holding the type and namespace universe, and beneath it the <see cref="GlobalScope"/>
    /// holding the project's implicit usings. File scopes hang off the latter.
    /// </summary>
    private static GlobalScope CreateGlobalScope(Model.ProgramModel programModel, DiagnosticBag diagnostics, CompilerOptions options)
    {
        var programScope = new ProgramScope(programModel);
        var globalScope = new GlobalScope(programScope, programModel, diagnostics);

        // Normalize: ImmutableArray default is a null-backed struct that throws on iteration.
        var globalUsings = options.GlobalUsings.IsDefault
            ? ImmutableArray<string>.Empty
            : options.GlobalUsings
            ;

        foreach(var ns in globalUsings)
        {
            globalScope.DeclareImport(ns);
        }

        return globalScope;
    }
}