using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;
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

        var globalScope = CreateGlobalScope(options);
        var programModel = new Model.ProgramModel();
        var context = new BuilderContext(globalScope, programModel, diagnostics, ambientProvider);
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

        // Phase 2a: Resolve member signatures on all registered types.
        var typeResolver = new TypeReferenceResolver(context);
        var concreteTypeBuilder = new ConcreteTypeBuilder(context, typeResolver);
        var abstractTypeBuilder = new AbstractTypeBuilder(context, typeResolver);
        var enumTypeBuilder = new EnumTypeBuilder(context, typeResolver);
        var newtypeBuilder = new NewtypeBuilder(context, typeResolver);
        var typeAliasBuilder = new TypeAliasBuilder(context, typeResolver);

        var allTypes = programModel.Namespaces.Values
            .SelectMany(ns => ns.Types.Values)
            .ToList()
            ;

        foreach(var typeDef in allTypes)
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
        var extensionBuilder = new ExtensionBuilder(context, typeResolver);
        extensionBuilder.BuildAll();

        // Phase 2c: Resolve retroactive conformance declarations (with blocks).
        var conformanceBuilder = new ConformanceBuilder(context, typeResolver);
        conformanceBuilder.BuildAll();

        return programModel;
    }

    private static Scope CreateGlobalScope(CompilerOptions options)
    {
        var globalScope = new Scope(parent: null);

        // Normalize: ImmutableArray default is a null-backed struct that throws on iteration.
        var globalUsings = options.GlobalUsings.IsDefault
            ? ImmutableArray<string>.Empty
            : options.GlobalUsings
            ;

        foreach(var ns in globalUsings)
        {
            globalScope.AddImportedNamespace(ns);
        }

        return globalScope;
    }
}