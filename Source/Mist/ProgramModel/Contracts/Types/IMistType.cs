namespace Mist.ProgramModel.Contracts.Types;

public interface IMistType
{
    string Name { get; }

    /// <summary>
    /// Includes any namespace and nesting path ("MyApp.Outer.Inner"); the key of the flat type registry.
    /// <para>
    /// Only a declared type is qualified by a NAMESPACE -- a tuple or a union has no namespace to be
    /// reachable through, and reports its structural name here. The namespace itself lives on the
    /// declared types alone.
    /// </para>
    /// </summary>
    string FullyQualifiedName { get; }

    IMistType? DeclaringType { get; }

    IReadOnlyList<IMistType> NestedTypes { get; }

    /// <summary>
    /// The single source of truth for where the type came from; a compiler-constructed type reports
    /// <see cref="MistAssemblyKind.Synthetic"/> rather than nothing.
    /// </summary>
    IAssembly Assembly { get; }
}