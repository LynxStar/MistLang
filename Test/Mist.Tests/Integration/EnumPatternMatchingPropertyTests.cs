// Feature: enums, Property 10: Pattern matching value equality
// **Validates: Requirements 8.1, 8.2, 8.3**

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

namespace Mist.Tests.Integration;

/// <summary>
/// Describes a single pattern matching scenario: an enum with members,
/// a scrutinee value, and the member being tested against.
/// </summary>
public sealed class PatternMatchScenario
{
    public string EnumName { get; }
    public IReadOnlyList<(string Name, int Value)> Members { get; }
    public int ScrutineeValue { get; }
    public int TestedMemberIndex { get; }

    public PatternMatchScenario(
        string enumName,
        IReadOnlyList<(string Name, int Value)> members,
        int scrutineeValue,
        int testedMemberIndex)
    {
        EnumName = enumName;
        Members = members;
        ScrutineeValue = scrutineeValue;
        TestedMemberIndex = testedMemberIndex;
    }

    public string TestedMemberName => Members[TestedMemberIndex].Name;
    public int TestedMemberValue => Members[TestedMemberIndex].Value;
    public bool ExpectedResult => ScrutineeValue == TestedMemberValue;

    public override string ToString()
    {
        return $"{ScrutineeValue} is {EnumName}.{TestedMemberName} (value={TestedMemberValue}) => {ExpectedResult}";
    }
}

/// <summary>
/// Batched property-based test for enum pattern matching value equality.
///
/// Property 10: Pattern matching value equality
///
/// For any enum member M with value V, the expression <c>x is EnumName.M</c>
/// SHALL evaluate to true when x equals V and false when x does not equal V.
///
/// The test generates 100 random scenarios, builds a single Mist program that
/// prints all 100 results (one per line), compiles once, runs once, and compares
/// each output line to the expected true/false result.
///
/// **Validates: Requirements 8.1, 8.2, 8.3**
/// </summary>
public sealed class EnumPatternMatchingPropertyTests : IDisposable
{
    private const int IterationCount = 100;

    // ─── Shared driver (reused across tests) ─────────────────────────────

    private static readonly CompilerDriver SharedDriver = new(
        new Mist.Lexing.MistLexer(),
        new Mist.Parsing.MistParser(),
        new SemanticModelBuilder(),
        ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
        ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));

    private readonly string outputDir;

    public EnumPatternMatchingPropertyTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "mist_pbt_enum_pm_" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(outputDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // ─── Known names for generation ─────────────────────────────────────

    private static readonly string[] EnumNames =
        ["Direction", "Color", "Status", "Priority", "Level"];

    private static readonly string[] MemberNamePool =
        ["North", "South", "East", "West", "Red", "Green", "Blue",
         "Active", "Inactive", "Pending", "High", "Low"];

    // ─── Batched program builder ─────────────────────────────────────────

    /// <summary>
    /// Builds a single Mist program that defines all unique enums needed by the
    /// scenarios and tests each pattern match, printing "True" or "False" per line.
    /// </summary>
    private static string BuildBatchedProgram(IReadOnlyList<PatternMatchScenario> scenarios)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("using Standard;");
        _ = sb.AppendLine();

        // Collect unique enum definitions (by name + members).
        // Since multiple scenarios may use the same enum name with different members,
        // we group by enum name and use the first occurrence's members.
        var enumDefs = new Dictionary<string, IReadOnlyList<(string Name, int Value)>>();
        foreach(var scenario in scenarios)
        {
            if(!enumDefs.ContainsKey(scenario.EnumName))
            {
                enumDefs[scenario.EnumName] = scenario.Members;
            }
        }

        // Emit enum declarations
        foreach(var (enumName, members) in enumDefs)
        {
            _ = sb.AppendLine($"public enum {enumName} : int {{");
            for(var i = 0; i < members.Count; i++)
            {
                var comma = i < members.Count - 1 ? "," : "";
                _ = sb.AppendLine($"    {members[i].Name} = {members[i].Value}{comma}");
            }

            _ = sb.AppendLine("}");
            _ = sb.AppendLine();
        }

        // Emit Program class with Main
        _ = sb.AppendLine("class Program {");
        _ = sb.AppendLine("    static void Main() {");

        for(var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            // Use the enum definition that was actually emitted (first occurrence for this name)
            var enumMembers = enumDefs[s.EnumName];
            var testedMember = s.Members[s.TestedMemberIndex];

            // Verify the tested member exists in the emitted enum
            var memberInEnum = enumMembers.FirstOrDefault(m => m.Name == testedMember.Name);

            // Create a variable holding the scrutinee value, then test with is-expression
            _ = sb.AppendLine($"        {s.EnumName} v{i} = {s.EnumName}.{enumMembers.First(m => m.Value == s.ScrutineeValue).Name};");
            _ = sb.AppendLine($"        bool r{i} = v{i} is {s.EnumName}.{testedMember.Name};");
            _ = sb.AppendLine($"        Console.WriteLine(r{i});");
        }

        _ = sb.AppendLine("    }");
        _ = sb.AppendLine("}");

        return sb.ToString();
    }

    /// <summary>
    /// Compiles a Mist source in-process via CompilerDriver, then runs the
    /// produced DLL with <c>dotnet</c> to capture stdout.
    /// </summary>
    private (int ExitCode, string Stdout, string Stderr) CompileAndRun(string mistSource)
    {
        var iterDir = Path.Combine(outputDir, Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(iterDir);

        try
        {
            var sourcePath = Path.Combine(iterDir, "Program.mist");
            File.WriteAllText(sourcePath, mistSource);

            var binDir = Path.Combine(iterDir, "bin");
            var options = new CompilerOptions(
                ProjectFolder: iterDir,
                SourcePaths: ImmutableArray.Create(sourcePath),
                Backend: new BackendId("csharp"),
                OutputDirectory: iterDir,
                BinDirectory: binDir,
                ProjectName: "TestProgram",
                NamespaceRoot: "TestProgram",
                EnabledAnalyzerIds: ImmutableArray<string>.Empty);

            var result = SharedDriver.Compile(options);

            if(!result.Succeeded || result.PrimaryOutputArtifactPath is null)
            {
                var diagnosticText = string.Join("\n",
                    result.Diagnostics.Select(d => $"  {d.Severity} {d.Id.Value}: {d.Message}"));
                return (-1, "", $"Compilation failed:\n{diagnosticText}");
            }

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = result.PrimaryOutputArtifactPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if(process is null)
            {
                return (-1, "", "Failed to start dotnet process");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();

            if(!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }

                return (-1, "", "Process timed out after 30 seconds");
            }

            return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
        }
        finally
        {
            try { Directory.Delete(iterDir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ─── Test ────────────────────────────────────────────────────────────

    /// <summary>
    /// Property 10: For any enum member M with value V, the expression
    /// <c>x is EnumName.M</c> evaluates to true when x equals V and false
    /// when x does not equal V. 100 random scenarios batched into a single
    /// compile + single run.
    ///
    /// **Validates: Requirements 8.1, 8.2, 8.3**
    /// </summary>
    [Fact]
    public void EnumPatternMatching_ValueEquality_MatchesCSharpSemantics()
    {
        // Generate scenarios ensuring all use consistent enum definitions.
        // We generate scenarios grouped by enum name so the batched program
        // uses a single definition per enum name.
        var scenarios = GenerateConsistentScenarios(IterationCount);

        var mistSource = BuildBatchedProgram(scenarios);
        var (exitCode, stdout, stderr) = CompileAndRun(mistSource);

        Assert.True(exitCode == 0,
            $"Mist program failed (exit code {exitCode}).\nStderr:\n{stderr}\nSource:\n{mistSource}");

        var lines = stdout.TrimEnd().Split('\n', StringSplitOptions.None);
        Assert.Equal(scenarios.Count, lines.Length);

        for(var i = 0; i < scenarios.Count; i++)
        {
            var expected = scenarios[i].ExpectedResult ? "True" : "False";
            var actual = lines[i].TrimEnd('\r');
            Assert.True(expected == actual,
                $"Scenario {i} failed: {scenarios[i]}\n" +
                $"Expected: '{expected}'\nActual:   '{actual}'");
        }
    }

    /// <summary>
    /// Generates scenarios that are consistent: all scenarios sharing the same
    /// enum name use the same member definitions, and scrutinee values reference
    /// members that exist in the emitted enum.
    /// </summary>
    private static List<PatternMatchScenario> GenerateConsistentScenarios(int count)
    {
        // First, generate fixed enum definitions for each enum name.
        var random = new Random(42); // deterministic seed for reproducibility
        var enumDefs = new Dictionary<string, (string Name, int Value)[]>();

        foreach(var enumName in EnumNames)
        {
            var memberCount = random.Next(2, 6); // 2-5 members
            var shuffled = MemberNamePool.OrderBy(_ => random.Next()).Take(memberCount).ToArray();
            var members = shuffled.Select((name, i) => (name, i)).ToArray();
            enumDefs[enumName] = members;
        }

        // Now generate scenarios that reference these fixed definitions.
        var scenarios = new List<PatternMatchScenario>(count);
        for(var i = 0; i < count; i++)
        {
            var enumName = EnumNames[random.Next(EnumNames.Length)];
            var members = enumDefs[enumName];
            var testedIdx = random.Next(members.Length);
            var shouldMatch = random.Next(2) == 0;

            int scrutineeValue;
            if(shouldMatch)
            {
                scrutineeValue = members[testedIdx].Value;
            }
            else
            {
                // Pick a different member's value to ensure mismatch
                var otherIdx = (testedIdx + 1) % members.Length;
                scrutineeValue = members[otherIdx].Value;
            }

            scenarios.Add(new PatternMatchScenario(
                enumName,
                members,
                scrutineeValue,
                testedIdx));
        }

        return scenarios;
    }
}