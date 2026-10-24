namespace Mist.Backends.CSharpTranspiler;

internal static class AssemblyConflictRule
{
    /// <summary>
    /// A file without an assembly definition has no version to disagree with, so it never conflicts and
    /// the copy already supplied is kept.
    /// </summary>
    public static bool Conflicts((string Name, Version? Version) supplied, (string Name, Version? Version) candidate)
    {
        return string.Equals(supplied.Name, candidate.Name, StringComparison.OrdinalIgnoreCase)
            && supplied.Version is not null
            && candidate.Version is not null
            && supplied.Version != candidate.Version;
    }
}