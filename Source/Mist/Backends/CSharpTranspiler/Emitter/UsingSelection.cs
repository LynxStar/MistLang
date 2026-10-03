using Mist.ProgramModel;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using CompilationUnit = Mist.ProgramModel.Model.CompilationUnit;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The imports an emitted file can carry, which are fewer than the ones Mist accepted. Mist judges a
/// using against its own namespaces, and a namespace whose declarations all erase -- aliases, <c>with</c>
/// declarations, intrinsics, a variadic family written as top-level monomorphs -- is one a Mist file may
/// import and a C# using cannot name. Dropping such a using loses nothing, since every type the emitter
/// writes is spelled in full. C# has a namespace when an emitted declaration sits in it, when a referenced
/// CLR assembly declares it, or when it encloses either, as <c>namespace A.B</c> declares <c>A</c> too.
/// Decided over the whole model before any file is written, so no file's answer depends on which
/// declarations were emitted ahead of it.
/// </summary>
internal sealed class UsingSelection
{
    private readonly HashSet<string> declared;

    private UsingSelection(HashSet<string> declared)
    {
        this.declared = declared;
    }

    public static UsingSelection Of(IProgramModel programModel)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);

        Declare(declared, WellKnownTypes.Void.Namespace);

        foreach(var unit in programModel.Assemblies.Where(assembly => assembly.HasMistSource).SelectMany(assembly => assembly.CompilationUnits))
        {
            foreach(var type in CSharpEmitterV2.WrittenDeclarations(unit))
            {
                Declare(declared, type.NamespaceName);
            }

            foreach(var block in unit.Extensions)
            {
                Declare(declared, block.NamespaceName);
            }
        }

        foreach(var type in programModel.TypesByFQN.Values.OfType<DeclaredType>())
        {
            if(type.Assembly.Kind == MistAssemblyKind.Ambient)
            {
                Declare(declared, type.NamespaceName);
            }
        }

        return new UsingSelection(declared);
    }

    public IReadOnlyList<string> Directives(CompilationUnit unit, IAssembly assembly)
    {
        return [.. unit.Usings.Concat(assembly.GlobalUsings)
            .Where(declared.Contains)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
    }

    public IReadOnlyList<string> StatementImports(CompilationUnit unit)
    {
        return [.. unit.Types
            .Select(type => type.NamespaceName)
            .Where(declared.Contains)
            .Distinct(StringComparer.Ordinal)];
    }

    private static void Declare(HashSet<string> declared, string namespaceName)
    {
        var name = namespaceName;
        while(name.Length > 0)
        {
            _ = declared.Add(name);

            var dot = name.LastIndexOf('.');
            name = dot < 0 ? "" : name[..dot];
        }
    }
}