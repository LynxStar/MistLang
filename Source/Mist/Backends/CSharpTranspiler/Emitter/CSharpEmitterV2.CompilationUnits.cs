using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal sealed partial class CSharpEmitterV2
{
    /// <summary>
    /// Emits a single compilation unit as one C# source file.
    /// </summary>
    private void EmitCompilationUnit(
        CompilationUnit unit,
        IAssembly assembly,
        bool emitsStatements,
        ConstraintClosureMap closures,
        UsingSelection usings,
        string outputDir,
        List<string> emittedFiles)
    {
        var emittableTypes = WrittenDeclarations(unit);
        var emittableBlocks = unit.Extensions;

        // A file that declares nothing emittable would produce a .cs holding only usings,
        // which Roslyn accepts but which adds a file to the compilation for no reason.
        if(emittableTypes.Count == 0 && emittableBlocks.Count == 0 && !emitsStatements)
        {
            return;
        }

        var writer = new CSharpWriter();

        EmitUsings(unit, assembly, usings, writer);

        if(emitsStatements)
        {
            EmitTopLevelStatements(unit, usings, writer);
        }

        if(emittableTypes.Count > 0 || emittableBlocks.Count > 0)
        {
            if(emitsStatements)
            {
                writer.WriteBlankLine();
            }

            EmitFileTypes(emittableTypes, emittableBlocks, emitsStatements, closures, writer);
        }

        // Determine output path: {outputDir}/{Assembly.Name}/{RelativePath}/{FileName}.cs
        var fileName = DeriveOutputFileName(unit);
        var pathSegments = new List<string> { outputDir, assembly.Name };
        if(!string.IsNullOrEmpty(unit.RelativePath))
        {
            pathSegments.Add(unit.RelativePath);
        }

        pathSegments.Add(fileName);
        var fullPath = Path.Combine([.. pathSegments]);

        var dir = Path.GetDirectoryName(fullPath);
        if(dir is not null)
        {
            _ = Directory.CreateDirectory(dir);
        }

        File.WriteAllText(fullPath, writer.ToString());
        emittedFiles.Add(fullPath);
    }

    /// <summary>
    /// The directives the Mist source wrote, the manifest's global usings included, because they are
    /// what an extension method or a bare ambient name in the body still binds through -- less those
    /// <see cref="UsingSelection"/> finds C# has no namespace for. Nothing is added: every name the
    /// emitter substitutes for a Mist type is written in full (<see cref="ClrSpelling"/>), so no
    /// directive exists for the emitter's own sake.
    /// </summary>
    private static void EmitUsings(CompilationUnit unit, IAssembly assembly, UsingSelection usings, CSharpWriter writer)
    {
        foreach(var ns in usings.Directives(unit, assembly))
        {
            writer.WriteLine($"using {IdentifierSpelling.Qualified(ns)};");
        }

        writer.WriteBlankLine();
    }

    private void EmitTopLevelStatements(CompilationUnit unit, UsingSelection usings, CSharpWriter writer)
    {
        var declared = usings.StatementImports(unit);

        foreach(var name in declared)
        {
            writer.WriteLine($"using {IdentifierSpelling.Qualified(name)};");
        }

        if(declared.Count > 0)
        {
            writer.WriteBlankLine();
        }

        if(unit.TopLevelMethod?.Body is not null)
        {
            BodyEmitter.EmitBlock(unit.TopLevelMethod.Body, writer, diagnostics);
        }
    }

    /// <summary>
    /// A file's types under the namespaces that declare them, one Mist file to one C# file.
    /// <para>
    /// The file-scoped form can be written ONCE and must precede every other member, so it serves
    /// exactly the file that declares one namespace and no top-level statements. Anything else needs
    /// blocks: a second namespace has nowhere to go, and statements already emitted are members the
    /// file-scoped form may not follow.
    /// </para>
    /// </summary>
    private void EmitFileTypes(
        List<DeclaredType> types,
        List<ExtensionBlock> blocks,
        bool hasTopLevelStatements,
        ConstraintClosureMap closures,
        CSharpWriter writer)
    {
        var namespaces = types.Select(type => type.NamespaceName)
            .Concat(blocks.Select(block => block.NamespaceName))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if(namespaces.Count == 1 && !hasTopLevelStatements)
        {
            writer.WriteLine($"namespace {IdentifierSpelling.Qualified(namespaces[0])};");
            writer.WriteBlankLine();
            EmitDeclarations(types, blocks, closures, writer);
            return;
        }

        var first = true;
        foreach(var namespaceName in namespaces)
        {
            if(!first)
            {
                writer.WriteBlankLine();
            }

            first = false;
            writer.WriteLine($"namespace {IdentifierSpelling.Qualified(namespaceName)}");
            using(writer.Block())
            {
                EmitDeclarations(
                    types.Where(type => type.NamespaceName == namespaceName).ToList(),
                    blocks.Where(block => block.NamespaceName == namespaceName).ToList(),
                    closures,
                    writer);
            }
        }
    }

    private void EmitDeclarations(
        IEnumerable<DeclaredType> types,
        IEnumerable<ExtensionBlock> blocks,
        ConstraintClosureMap closures,
        CSharpWriter writer)
    {
        var anyEmitted = false;
        foreach(var type in types)
        {
            if(type is MemberType member && UnionSpecialization.Specializes(member))
            {
                foreach(var instance in UnionSpecialization.InstancesOf(member))
                {
                    using var specializing = TypeEmitter.Specializing(instance);
                    TypeEmitter.Emit(type, closures, variadicUses, writer, diagnostics, ref anyEmitted);
                }

                continue;
            }

            TypeEmitter.Emit(type, closures, variadicUses, writer, diagnostics, ref anyEmitted);
        }

        foreach(var block in blocks)
        {
            ExtensionBlockEmitter.Emit(block, writer, diagnostics, ref anyEmitted, variadicUses);
        }
    }

    private static string DeriveOutputFileName(CompilationUnit unit)
    {
        return $"{Path.GetFileNameWithoutExtension(unit.SourcePath)}.cs";
    }
}