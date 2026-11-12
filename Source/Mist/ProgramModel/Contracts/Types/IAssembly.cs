using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A declaration boundary: every declared type belongs to exactly one, so which project declared it is
/// modelled rather than re-derived from file paths.
/// <para>
/// A <em>logical</em> boundary. Referenced Mist projects are emitted into the compiled project's CLR
/// assembly, so an assembly here is not one emitted DLL and any rule against it is enforced by the Mist
/// frontend alone. Per-assembly emission is Carbonek task <c>MIST-172</c>.
/// </para>
/// </summary>
public interface IAssembly
{
    /// <summary>
    /// For <see cref="MistAssemblyKind.Self"/>, the compiled project's name and so the emitted CLR
    /// assembly name.
    /// </summary>
    string Name { get; }

    MistAssemblyKind Kind { get; }

    /// <summary>
    /// The canonical <c>mist.toml</c> path, for <see cref="MistAssemblyKind.Reference"/> only: every other
    /// kind is singular and needs no further discriminator.
    /// </summary>
    string? ProjectPath { get; }

    /// <summary>
    /// Whether the compiler resolves members on and emits this assembly's types. False for CLR-projected
    /// and compiler-constructed types, which have no Mist declaration to work from.
    /// </summary>
    bool HasMistSource { get; }

    IReadOnlyList<IMethod> EntryPoints { get; }

    /// <summary>
    /// Declared in the project's <c>mist.toml</c>. On the contract because a file reaching a CLR type
    /// through one names it nowhere, so the emitted C# has no other record of the import.
    /// </summary>
    IReadOnlyList<string> GlobalUsings { get; }

    IReadOnlyList<CompilationUnit> CompilationUnits { get; }
}