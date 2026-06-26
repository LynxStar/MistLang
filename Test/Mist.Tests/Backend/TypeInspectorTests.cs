// Feature: csharp-interop-v2, Task 8.4
// In-process tests for TypeInspector.Inspect — invokes the report builder
// directly with a StringWriter, no process spawning.
//
// Validates: Requirements 10.2, 10.5, 10.7, 13.8

using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;

namespace Mist.Tests.Backend;

/// <summary>
/// In-process tests for <see cref="TypeInspector.Inspect"/>. Tests call the
/// inspector directly with a <see cref="StringWriter"/> and assert the output
/// content, verifying type header/kind, visible member listing, hidden members
/// with reasons, inherited members with base FQN annotations, ordering, and
/// the unresolved-type error path.
/// </summary>
public sealed class TypeInspectorTests
{
    private const string TargetFramework = "net10.0";
    private static readonly ImmutableArray<string> NoAdditionalPaths = ImmutableArray<string>.Empty;

    // ═══════════════════════════════════════════════════════════════════════
    // Test 4: Inspect System.Console → output contains "System.Console (class)",
    //          visible methods include "WriteLine", hidden members section shows
    //          some hidden members.
    //          Validates: Requirements 10.2, 10.5
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InspectSystemConsole_ContainsClassHeaderAndWriteLineAndHiddenMembers()
    {
        var output = new StringWriter();

        var exitCode = TypeInspector.Inspect(TargetFramework, NoAdditionalPaths, "System.Console", output);

        Assert.Equal(0, exitCode);
        var text = output.ToString();

        // Type header with kind
        Assert.Contains("System.Console (class)", text);

        // Visible methods include WriteLine
        Assert.Contains("WriteLine", text);
        Assert.Contains("VISIBLE:", text);

        // Hidden members section present
        Assert.Contains("HIDDEN:", text);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 5: Inspect ordering — visible properties/methods in ascending ordinal
    //          by name.
    //          Validates: Requirement 10.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InspectSystemConsole_VisibleMethodsInAscendingNameOrder()
    {
        var output = new StringWriter();

        var exitCode = TypeInspector.Inspect(TargetFramework, NoAdditionalPaths, "System.Console", output);

        Assert.Equal(0, exitCode);
        var text = output.ToString();

        // Extract method lines from the VISIBLE section
        var lines = text.Split('\n');
        var methodLines = new List<string>();
        var inVisible = false;
        foreach(var line in lines)
        {
            if(line.TrimStart().StartsWith("VISIBLE:"))
            {
                inVisible = true;
                continue;
            }
            if(line.TrimStart().StartsWith("HIDDEN:"))
            {
                break;
            }

            if(inVisible && line.Contains("method "))
            {
                methodLines.Add(line);
            }
        }

        // Verify they are sorted ascending by name
        Assert.NotEmpty(methodLines);
        var methodNames = methodLines
            .Select(l =>
            {
                var trimmed = l.TrimStart();
                // Format: "    method Name(params) -> ReturnType"
                var afterMethod = trimmed["method ".Length..];
                var parenIdx = afterMethod.IndexOf('(');
                return parenIdx > 0 ? afterMethod[..parenIdx] : afterMethod;
            })
            .ToList();

        for(var i = 1; i < methodNames.Count; i++)
        {
            Assert.True(
                string.Compare(methodNames[i - 1], methodNames[i], StringComparison.Ordinal) <= 0,
                $"Methods not in ascending order: '{methodNames[i - 1]}' should come before '{methodNames[i]}'");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 6: Inspect hidden reasons — assert at least one hidden member has
    //          a reason string (e.g. "HIDDEN: non-public" or "HIDDEN: pointer
    //          in signature" or "HIDDEN: event").
    //          Validates: Requirement 10.5
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InspectSystemConsole_HiddenMembersHaveReasonStrings()
    {
        var output = new StringWriter();

        var exitCode = TypeInspector.Inspect(TargetFramework, NoAdditionalPaths, "System.Console", output);

        Assert.Equal(0, exitCode);
        var text = output.ToString();

        // Extract lines from the HIDDEN section
        var lines = text.Split('\n');
        var hiddenLines = new List<string>();
        var inHidden = false;
        foreach(var line in lines)
        {
            if(line.TrimStart().StartsWith("HIDDEN:") && !line.Contains(":") == false)
            {
                // Check if this is the section header
                if(line.Trim() == "HIDDEN:")
                {
                    inHidden = true;
                    continue;
                }
            }
            if(inHidden && line.Trim().Length > 0 && !line.TrimStart().StartsWith("VISIBLE:"))
            {
                hiddenLines.Add(line);
            }
        }

        // At least one hidden member should have a known reason
        var knownReasons = new[] { "HIDDEN: non-public", "HIDDEN: event", "HIDDEN: pointer in signature",
                                    "HIDDEN: out parameter", "HIDDEN: ref readonly return",
                                    "HIDDEN: ref struct in signature", "HIDDEN: signature uses" };

        Assert.True(
            hiddenLines.Any(line => knownReasons.Any(reason => line.Contains(reason))),
            $"Expected at least one hidden member with a known reason. Hidden lines:\n{string.Join("\n", hiddenLines)}");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 7: Inspect inherited member — assert at least one member shows
    //          [from ...] annotation.
    //          Validates: Requirement 10.7
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InspectSystemConsole_InheritedMembersShowFromAnnotation()
    {
        // System.Console inherits from System.Object, so methods like ToString,
        // Equals, GetHashCode should be inherited. Let's use a type with richer
        // inheritance like System.IO.MemoryStream which inherits from Stream.
        var output = new StringWriter();

        var exitCode = TypeInspector.Inspect(TargetFramework, NoAdditionalPaths, "System.IO.MemoryStream", output);

        Assert.Equal(0, exitCode);
        var text = output.ToString();

        // At least one member should show [from ...] annotation for inherited members
        Assert.Contains("[from ", text);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 8: Inspect unresolved type → returns 1, error output to stderr.
    //          Validates: Requirement 10.7 (unresolved path)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InspectUnresolvedType_ReturnsOne()
    {
        var output = new StringWriter();

        // Capture Console.Error output
        var originalError = Console.Error;
        var errorWriter = new StringWriter();
        Console.SetError(errorWriter);

        try
        {
            var exitCode = TypeInspector.Inspect(
                TargetFramework,
                NoAdditionalPaths,
                "Nonexistent.Bogus.TypeThatDoesNotExist",
                output);

            Assert.Equal(1, exitCode);
            var errorText = errorWriter.ToString();
            Assert.Contains("Nonexistent.Bogus.TypeThatDoesNotExist", errorText);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 9: Inspect missing name — TypeInspector.Inspect with a type name
    //          that has no namespace separator returns 1.
    //          (Tests the validation path in TypeInspector.Inspect where
    //          lastDot <= 0 signals an invalid type name.)
    //          Validates: Requirement 13.8
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void InspectMissingOrInvalidTypeName_ReturnsOne()
    {
        var output = new StringWriter();

        // Capture Console.Error output
        var originalError = Console.Error;
        var errorWriter = new StringWriter();
        Console.SetError(errorWriter);

        try
        {
            // A type name with no dot — simulates the "missing name" validation path.
            // TypeInspector.Inspect checks lastDot <= 0 and returns 1.
            var exitCode = TypeInspector.Inspect(
                TargetFramework,
                NoAdditionalPaths,
                "NoNamespace",
                output);

            Assert.Equal(1, exitCode);
            var errorText = errorWriter.ToString();
            Assert.Contains("NoNamespace", errorText);
        }
        finally
        {
            Console.SetError(originalError);
        }
    }
}