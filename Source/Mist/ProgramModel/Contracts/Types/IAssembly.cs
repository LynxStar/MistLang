using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A declaration boundary within a compilation. Every declared type belongs to exactly
/// one assembly, and the assembly is what makes "which project declared this?" a
/// modelled fact rather than something each consumer re-derives from file paths.
/// <para>
/// This is a <em>logical</em> boundary. Referenced Mist projects are currently emitted
/// into a single CLR assembly alongside the project being compiled, so an assembly here
/// does not correspond one-to-one with an emitted DLL. Splitting emission per assembly
/// is tracked in <c>backlog/issues.yaml MIST-172</c>; until then any rule expressed
/// against this boundary is enforced by the Mist frontend alone.
/// </para>
/// </summary>
public interface IAssembly
{
    /// <summary>
    /// The assembly's name. For <see cref="MistAssemblyKind.Self"/> this is the compiled
    /// project's name, which is also the emitted CLR assembly name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Where this assembly came from relative to the compilation.
    /// </summary>
    MistAssemblyKind Kind { get; }

    /// <summary>
    /// The canonical <c>mist.toml</c> path that identifies this project, for
    /// <see cref="MistAssemblyKind.Reference"/> assemblies. Null for every other kind:
    /// the compiled project, the standard library, ambient metadata and synthetic types
    /// are each singular and need no further discriminator.
    /// </summary>
    string? ProjectPath { get; }

    /// <summary>
    /// Whether this assembly's types are declared in Mist source, and so are the
    /// compiler's to resolve members on and emit. False for CLR-projected and
    /// compiler-constructed types, which have no Mist declaration to work from.
    /// </summary>
    bool HasMistSource { get; }

    /// <summary>
    /// The methods of this assembly with the shape of an entry point, empty if it has none.
    /// </summary>
    IReadOnlyList<IMethod> EntryPoints { get; }

    /// <summary>
    /// The implicit usings every file in this assembly resolves through, declared in the project's
    /// <c>mist.toml</c>. On the contract because a backend needs them: a file reaching a CLR type
    /// through one of these names it nowhere, so the emitted C# has no other record that the import
    /// happened.
    /// </summary>
    IReadOnlyList<string> GlobalUsings { get; }

    IReadOnlyList<CompilationUnit> CompilationUnits { get; }
}