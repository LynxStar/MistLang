using System.Collections.Immutable;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// The program model's record of a single Mist source file. Preserves file scope into
/// emission: the file's using directives, its types in declaration order, and its
/// top-level statements if any.
/// </summary>
internal sealed class CompilationUnit
{
    /// <summary>
    /// The path to the source file on disk. The emitted C# file takes its name from this,
    /// which is what makes generated output traceable back to the Mist file it came from.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// This file's directory relative to the project root, or empty for a file at the root.
    /// Carried so emitted output mirrors the source tree rather than flattening it, which
    /// would collide two same-named files in different folders.
    /// </summary>
    public string RelativePath { get; init; } = "";

    /// <summary>
    /// The namespace names brought into scope by this file's own <c>using</c> directives.
    /// Excludes the global usings from <c>mist.toml</c>, which belong to the assembly: mixing
    /// them in here would emit one project's implicit usings into another project's files.
    /// </summary>
    public ImmutableArray<string> Usings { get; init; } = [];

    /// <summary>
    /// Member types declared in this file, in declaration order.
    /// </summary>
    public List<MemberType> Types { get; } = [];

    /// <summary>
    /// Whether this unit contains top-level statements.
    /// </summary>
    public bool HasTopLevelStatements { get; init; }

    /// <summary>
    /// The synthetic <c>Main</c> holding the bound top-level statements, non-null exactly
    /// when <see cref="HasTopLevelStatements"/> is true.
    /// <para>
    /// A method rather than loose statements because binding works on delegate bodies: giving
    /// the statements a method to live in is what lets Phase 3 resolve them through the same
    /// path as any other body, with no special case.
    /// </para>
    /// </summary>
    public Method? TopLevelMethod { get; init; }
}