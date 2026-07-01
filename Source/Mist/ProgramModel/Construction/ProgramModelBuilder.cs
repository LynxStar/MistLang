using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Model;

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
    /// <returns>The built program model, or null on fatal failure.</returns>
    public static IProgramModel? Build(
        MistCompilation compilation,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        CompilerOptions options,
        DiagnosticBag diagnostics)
    {
        var globalScope = CreateGlobalScope(options);
        var programModel = new Model.ProgramModel();

        // Process each compilation unit.
        for(var i = 0; i < asts.Length; i++)
        {
            var compilationUnit = (CompilationUnitNode)asts[i];

            var unit = i < compilation.Units.Length ? compilation.Units[i] : null;
            var sourceText = unit?.Source;
            var effectiveNamespaceRoot = unit?.NamespaceRoot ?? options.NamespaceRoot;
            var effectiveProjectFolder = unit?.ProjectFolder ?? options.ProjectFolder;

            var unitBuilder = new CompilationUnitBuilder(
                globalScope,
                programModel,
                diagnostics,
                sourceText,
                effectiveNamespaceRoot,
                effectiveProjectFolder);

            _ = unitBuilder.Build(compilationUnit);
        }

        return programModel;
    }

    private static Scope CreateGlobalScope(CompilerOptions options)
    {
        var globalScope = new Scope(parent: null);

        // Normalize: ImmutableArray default is a null-backed struct that throws on iteration.
        var globalUsings = options.GlobalUsings.IsDefault
            ? ImmutableArray<string>.Empty
            : options.GlobalUsings;

        foreach(var ns in globalUsings)
        {
            globalScope.AddImportedNamespace(ns);
        }

        return globalScope;
    }
}