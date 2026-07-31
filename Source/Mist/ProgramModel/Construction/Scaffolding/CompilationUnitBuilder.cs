using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Construction.Scaffolding;

/// <summary>
/// Processes compilation units (files) by creating the file scope and delegating
/// to specialized builders. One instance is created per compilation and reused
/// across all files.
/// </summary>
internal sealed class CompilationUnitBuilder
{
    private readonly BuilderContext context;
    private readonly NamespaceBuilder namespaceBuilder;
    private readonly TypeBuilder typeBuilder;

    public CompilationUnitBuilder(BuilderContext context)
    {
        this.context = context;
        namespaceBuilder = new NamespaceBuilder(context);
        typeBuilder = new TypeBuilder(context);
    }

    /// <summary>
    /// Processes a compilation unit: creates the file scope, resolves namespaces
    /// and imports, registers type declarations, and stashes a
    /// <see cref="ProgramModelCompilationUnit"/> on the assembly.
    /// Returns the file scope, or null if namespace validation produced fatal errors.
    /// </summary>
    public Scope? Build(CompilationUnit unit)
    {
        var ast = (CompilationUnitNode)unit.Ast!;
        var sourceText = unit.Source;
        var namespaceRoot = unit.NamespaceRoot!;
        var projectFolder = unit.ProjectFolder!;

        // The assembly is resolved before the file scope rather than with the type registration
        // below, because it decides which implicit usings this file resolves through.
        var assembly = ResolveAssembly(unit);
        var fileScope = new FileScope(context.ScopeFor(assembly), context.ProgramModel, context.Diagnostics);

        // Validate namespaces and populate the file's usings.
        var fileNamespace = namespaceBuilder.Build(fileScope, ast, sourceText, namespaceRoot, projectFolder);
        if(fileNamespace is null)
        {
            return null;
        }

        // Register type declarations.
        var result = typeBuilder.Build(fileScope, fileNamespace, ast, assembly);

        // Read off the scope AFTER namespaceBuilder ran, which is what populated the imports.
        var relativePath = ComputeRelativePath(sourceText.Path, unit.ProjectFolder);
        var modelUnit = new ProgramModelCompilationUnit
        {
            SourcePath = sourceText.Path,
            RelativePath = relativePath,
            Namespace = fileNamespace,
            Usings = [.. fileScope.Imports],
            HasTopLevelStatements = result.TopLevelMethod is not null,
            TopLevelMethod = result.TopLevelMethod,
        };
        modelUnit.Types.AddRange(result.Types);
        assembly.CompilationUnits.Add(modelUnit);

        return fileScope;
    }

    /// <summary>
    /// Determines which assembly a unit's declarations belong to. The unit carries two
    /// independent flags rather than one boundary, so they are folded here: stdlib units
    /// are the standard library, units with no origin project belong to the project being
    /// compiled, and the rest arrived through a reference.
    /// </summary>
    private MistAssembly ResolveAssembly(CompilationUnit unit)
    {
        var programModel = context.ProgramModel;

        if(unit.IsStdlib)
        {
            return programModel.StandardAssembly
                ?? throw new InvalidOperationException(
                    "ICE: a stdlib unit was processed but the compilation did not report "
                    + "loading the standard library.");
        }

        if(unit.OriginProjectPath is null)
        {
            return programModel.SelfAssembly
                ?? throw new InvalidOperationException(
                    "ICE: a compilation unit was processed before the self assembly was established.");
        }

        // Referenced assemblies come from the compilation's project list, which the driver
        // populates alongside the units themselves. A unit citing a project that is not
        // there means the two disagree.
        return programModel.FindReferenceAssembly(unit.OriginProjectPath)
            ?? throw new InvalidOperationException(
                $"ICE: unit '{unit.Source.Path}' cites referenced project "
                + $"'{unit.OriginProjectPath}', which is absent from the compilation's project list.");
    }

    /// <summary>
    /// Computes the directory of the source file relative to the project root.
    /// Returns empty string for files at the root.
    /// </summary>
    private static string ComputeRelativePath(string? sourcePath, string? projectFolder)
    {
        if(string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(projectFolder))
        {
            return "";
        }

        var fileDir = Path.GetDirectoryName(sourcePath);
        if(string.IsNullOrEmpty(fileDir))
        {
            return "";
        }

        var relative = Path.GetRelativePath(projectFolder, fileDir);
        return relative == "." ? "" : relative;
    }
}