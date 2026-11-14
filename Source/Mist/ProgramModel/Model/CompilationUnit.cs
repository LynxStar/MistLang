using System.Collections.Immutable;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// One Mist source file, preserving its file scope into emission.
/// </summary>
public sealed class CompilationUnit
{
    /// <summary>
    /// The emitted C# file takes its name from this, which traces output back to its Mist file.
    /// </summary>
    public required string SourcePath { get; init; }

    /// <summary>
    /// Empty for a file at the project root. Emitted output mirrors the source tree by this, since
    /// flattening it would collide two same-named files in different folders.
    /// </summary>
    public string RelativePath { get; init; } = "";

    /// <summary>
    /// This file's own <c>using</c> directives only. The <c>mist.toml</c> global usings belong to the
    /// assembly: mixing them in would emit one project's implicit usings into another project's files.
    /// </summary>
    public ImmutableArray<string> Usings { get; init; } = [];

    /// <summary>
    /// Types declared at this file's top level, in declaration order. Every DECLARED kind, not only
    /// the ones with members: an enum is emitted too, and a newtype or alias has to be seen in order
    /// to be deliberately erased rather than silently missed.
    /// <para>
    /// Nested types are absent by design, reached instead through their declaring type's
    /// <see cref="MistType.NestedTypes"/>, since emitting one means emitting it INSIDE that type.
    /// </para>
    /// </summary>
    internal List<DeclaredType> Types { get; } = [];

    /// <summary>
    /// Extension blocks written at this file's top level, in declaration order. Kept apart from
    /// <see cref="Types"/> because a block is not a declared type -- it declares no type, is not
    /// registered by name, and is reached only through the namespace registry or through here.
    /// <para>
    /// A file may hold nothing else: an extensions-only file still emits, so anything counting what
    /// a unit produces has to count both lists.
    /// </para>
    /// </summary>
    internal List<ExtensionBlock> Extensions { get; } = [];

    public bool HasTopLevelStatements { get; init; }

    /// <summary>
    /// The outermost block namespace declarations in this file, by declared name and in declaration
    /// order: those written at the file's top level and those directly inside its file-scoped
    /// namespace. Recorded for every file, since whether they may sit beside
    /// <see cref="HasTopLevelStatements"/> is the entry-point rule's to judge.
    /// </summary>
    public ImmutableArray<string> BlockNamespaces { get; init; } = [];

    /// <summary>
    /// The synthetic <c>Main</c> holding the bound top-level statements, non-null exactly
    /// when <see cref="HasTopLevelStatements"/> is true.
    /// <para>
    /// A method rather than loose statements because binding works on delegate bodies: giving
    /// the statements a method to live in is what lets Phase 3 resolve them through the same
    /// path as any other body, with no special case.
    /// </para>
    /// </summary>
    internal Method? TopLevelMethod { get; init; }
}