// Feature: inferred-namespaces, Property 3: Inferred Namespace Computation
// For any valid namespace root and file path relative to the project folder,
// the computed inferred namespace follows the formula {root}.{folder1}.{folder2}...
// and equals root for project-root files.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Semantics.Model;

namespace Mist.Tests.Semantics;

/// <summary>
/// Property-based tests for <see cref="SemanticModelBuilder.ComputeInferredNamespace"/>.
/// </summary>
public sealed class InferredNamespaceComputationPropertyTests
{
    private static readonly char[] ValidNamespaceChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();

    private static readonly char[] ValidFolderChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_".ToCharArray();

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record ValidNamespaceRoot(string Value)
    {
        public override string ToString()
        {
            return Value;
        }
    }

#pragma warning disable CA1819
    public record FolderSegments(string[] Segments)
    {
        public override string ToString()
        {
            return string.Join("/", Segments);
        }
    }
#pragma warning restore CA1819
#pragma warning restore CA1034

    // ─── Arbitrary factories ─────────────────────────────────────────────

    public static Arbitrary<ValidNamespaceRoot> ArbitraryValidNamespaceRoot()
    {
        var gen = from len in Gen.Choose(1, 64)
                  from chars in Gen.ArrayOf(Gen.Elements(ValidNamespaceChars), len)
                  select new ValidNamespaceRoot(new string(chars));
        return gen.ToArbitrary();
    }

    public static Arbitrary<FolderSegments> ArbitraryFolderSegments()
    {
        var segmentGen = from len in Gen.Choose(1, 32)
                         from chars in Gen.ArrayOf(Gen.Elements(ValidFolderChars), len)
                         select new string(chars);

        var gen = from count in Gen.Choose(1, 5)
                  from segments in Gen.ArrayOf(segmentGen, count)
                  select new FolderSegments(segments);
        return gen.ToArbitrary();
    }

    // ─── Property 3a: File at project root returns namespace root ────────

    /// <summary>
    /// For any valid namespace root, a file located directly in the project folder
    /// should have an inferred namespace equal to the namespace root.
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InferredNamespaceComputationPropertyTests) }, MaxTest = 100)]
    public bool FileAtProjectRoot_ReturnsNamespaceRoot(ValidNamespaceRoot nsRoot)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var filePath = Path.Combine(projectFolder, "Program.mist");

        var result = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        return result == nsRoot.Value;
    }

    // ─── Property 3b: File in subdirectory appends folder segments ──────

    /// <summary>
    /// For any valid namespace root and non-empty folder path, a file in a subdirectory
    /// should have an inferred namespace of {root}.{folder1}.{folder2}...{folderN}.
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InferredNamespaceComputationPropertyTests) }, MaxTest = 100)]
    public bool FileInSubdirectory_AppendsSegments(ValidNamespaceRoot nsRoot, FolderSegments folders)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = Path.Combine(folders.Segments);
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var result = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        var expectedSegments = string.Join(".", folders.Segments);
        var expected = $"{nsRoot.Value}.{expectedSegments}";

        return result == expected;
    }

    // ─── Property 3c: Path separators are normalized to dots ────────────

    /// <summary>
    /// For any valid namespace root and folder segments, the computed namespace
    /// uses dots as separators regardless of the OS path separator character.
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InferredNamespaceComputationPropertyTests) }, MaxTest = 100)]
    public bool PathSeparators_NormalizedToDots(ValidNamespaceRoot nsRoot, FolderSegments folders)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = Path.Combine(folders.Segments);
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var result = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        // The result should never contain path separator characters
        return !result.Contains(Path.DirectorySeparatorChar)
            && !result.Contains(Path.AltDirectorySeparatorChar);
    }

    // ─── Property 3d: Result always starts with namespace root ──────────

    /// <summary>
    /// For any valid namespace root and any file path, the computed namespace
    /// always starts with the namespace root.
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InferredNamespaceComputationPropertyTests) }, MaxTest = 100)]
    public bool Result_AlwaysStartsWithNamespaceRoot(ValidNamespaceRoot nsRoot, FolderSegments folders)
    {
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = Path.Combine(folders.Segments);
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var result = SemanticModelBuilder.ComputeInferredNamespace(
            nsRoot.Value, projectFolder, filePath);

        return result.StartsWith(nsRoot.Value, StringComparison.Ordinal);
    }

    // ─── Property 3e: Segment count matches folder depth ────────────────

    /// <summary>
    /// For any valid namespace root and folder segments, the number of dot-separated
    /// segments in the result equals 1 (root) + number of folder segments.
    /// **Validates: Requirements 3.1, 3.2, 3.3, 3.4, 3.5**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(InferredNamespaceComputationPropertyTests) }, MaxTest = 100)]
    public bool SegmentCount_MatchesFolderDepthPlusRoot(FolderSegments folders)
    {
        // Use a namespace root without dots to make counting straightforward
        var simpleRoot = "TestRoot";
        var projectFolder = Path.Combine(Path.GetTempPath(), "project");
        var subPath = Path.Combine(folders.Segments);
        var filePath = Path.Combine(projectFolder, subPath, "File.mist");

        var result = SemanticModelBuilder.ComputeInferredNamespace(
            simpleRoot, projectFolder, filePath);

        var segments = result.Split('.');
        // Expected: "TestRoot" + each folder segment
        return segments.Length == 1 + folders.Segments.Length
            && segments[0] == simpleRoot;
    }
}