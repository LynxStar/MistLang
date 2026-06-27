// UnusedParameterRemover — Iteratively removes unused parameters (IDE0060)
// and their corresponding arguments at all call sites until the solution is clean.
//
// Strategy:
//   1. Shell out to `dotnet format --verify-no-changes --diagnostics IDE0060`
//      to discover violations (supports .slnx, respects .editorconfig).
//   2. Parse the error output to extract file, line, column, and parameter name.
//   3. For each violation, use Roslyn to parse the file, identify the parameter,
//      find the containing method, and locate all call sites across the solution.
//   4. Remove the parameter and corresponding arguments at all call sites.
//   5. Loop until clean or max iterations reached.
//
// Usage:
//   dotnet run --project tools/UnusedParameterRemover [path-to-solution.slnx]

using System.Collections.Immutable;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.FindSymbols;
using Microsoft.CodeAnalysis.MSBuild;
using Microsoft.CodeAnalysis.Text;

MSBuildLocator.RegisterDefaults();

var solutionPath = args.Length > 0
    ? args[0]
    : Path.Combine(Directory.GetCurrentDirectory(), "Mist.slnx");

if (!File.Exists(solutionPath))
{
    Console.Error.WriteLine($"Solution not found: {solutionPath}");
    return 1;
}

Console.WriteLine($"Solution: {solutionPath}");

var iteration = 0;
const int maxIterations = 20;

while (iteration < maxIterations)
{
    iteration++;
    Console.WriteLine($"\n=== Iteration {iteration} ===");

    // Step 1: Run dotnet format to discover IDE0060 violations.
    var violations = RunDotnetFormat(solutionPath);

    if (violations.Count == 0)
    {
        Console.WriteLine("No unused parameters found. Done!");
        return 0;
    }

    Console.WriteLine($"  Found {violations.Count} violation(s).");

    // Step 2: For each violation, parse the file, find the parameter, resolve
    // the containing method, and find all call sites using MSBuildWorkspace.
    var editsApplied = await ProcessViolations(solutionPath, violations);

    if (editsApplied == 0)
    {
        Console.WriteLine("  No edits could be applied. Stopping to avoid infinite loop.");
        return 1;
    }

    Console.WriteLine($"  Applied edits to {editsApplied} file(s).");

    // Post-pass: check for CS1501 errors (wrong argument count) and fix them.
    // This catches call sites that SymbolFinder missed (common in same-file refs).
    Console.WriteLine("  Running post-pass build check...");
    var buildErrors = RunBuildAndCollectErrors(solutionPath);
    if (buildErrors.Count > 0)
    {
        Console.Error.WriteLine($"  BUILD FAILED with {buildErrors.Count} error(s) after edits. Stopping.");
        foreach (var err in buildErrors.Take(10))
        {
            Console.Error.WriteLine($"    {Path.GetFileName(err.FilePath)}({err.Line},{err.Column}): {err.MethodName}");
        }

        return 1;
    }
    else
    {
        Console.WriteLine("  Build clean after edits.");
    }
}

Console.Error.WriteLine($"Reached max iterations ({maxIterations}). Some violations may remain.");
return 1;

// ---------------------------------------------------------------------------
// Shell out to `dotnet format` and parse IDE0060 diagnostics from stderr/stdout.
// ---------------------------------------------------------------------------
static List<FormatViolation> RunDotnetFormat(string solutionPath)
{
    Console.WriteLine("  Running dotnet format...");
    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"format \"{solutionPath}\" --verify-no-changes --diagnostics IDE0060",
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    using var process = Process.Start(psi)!;

    // Read both streams asynchronously to avoid deadlocks when output is large.
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    var stdout = stdoutTask.GetAwaiter().GetResult();
    var stderr = stderrTask.GetAwaiter().GetResult();

    Console.WriteLine("  dotnet format complete.");

    var combined = stdout + "\n" + stderr;
    return ParseViolations(combined);
}

static List<FormatViolation> ParseViolations(string output)
{
    var results = new List<FormatViolation>();

    // Pattern: /path/to/file.cs(line,col): error IDE0060: Remove unused parameter 'paramName' ...
    var regex = new Regex(
        @"^(.+?)\((\d+),(\d+)\): error IDE0060: Remove unused parameter '(\w+)'",
        RegexOptions.Multiline);

    foreach (Match match in regex.Matches(output))
    {
        var filePath = match.Groups[1].Value.Trim();

        results.Add(new FormatViolation(
            FilePath: filePath,
            Line: int.Parse(match.Groups[2].Value),
            Column: int.Parse(match.Groups[3].Value),
            ParameterName: match.Groups[4].Value));
    }

    return results;
}

// ---------------------------------------------------------------------------
// Process violations: parse files, find parameters, trace call sites, apply edits.
// ---------------------------------------------------------------------------
static async Task<int> ProcessViolations(string solutionPath, List<FormatViolation> violations)
{
    // Group violations by containing project so we can open workspaces efficiently.
    // First, figure out which projects contain these files.
    var projectPaths = DiscoverProjectPaths(solutionPath);

    // Open the solution workspace once for call-site resolution.
    // Since .slnx isn't supported by MSBuildWorkspace, we open projects individually
    // and combine them.
    using var workspace = MSBuildWorkspace.Create();
    workspace.WorkspaceFailed += (_, e) =>
    {
        // Silence non-fatal warnings.
    };

    // Load all projects into a single solution for cross-project reference finding.
    Solution? solution = null;
    foreach (var projectPath in projectPaths)
    {
        try
        {
            Console.WriteLine($"    Loading: {Path.GetFileName(projectPath)}");
            var project = await workspace.OpenProjectAsync(projectPath);
            solution = workspace.CurrentSolution;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Failed to load project {projectPath}: {ex.Message}");
        }
    }

    if (solution is null)
    {
        Console.Error.WriteLine("  Failed to load any projects.");
        return 0;
    }

    Console.WriteLine($"    Loaded {solution.Projects.Count()} project(s).");

    // Build a map of file path -> Document for quick lookup.
    var documentsByPath = new Dictionary<string, Document>(StringComparer.OrdinalIgnoreCase);
    foreach (var project in solution.Projects)
    {
        foreach (var document in project.Documents)
        {
            if (document.FilePath is not null)
            {
                documentsByPath[document.FilePath] = document;
            }
        }
    }

    // Collect all edits across all files.
    var editsByFile = new Dictionary<string, List<TextSpan>>(StringComparer.OrdinalIgnoreCase);

    // Process violations, but only ONE parameter per method per iteration.
    // This avoids ordinal conflicts when multiple params are removed from
    // the same method (the remaining ones cascade in subsequent iterations).
    var processedMethods = new HashSet<string>();

    foreach (var violation in violations)
    {
        Console.WriteLine($"    Processing: {violation.ParameterName} in {Path.GetFileName(violation.FilePath)}:{violation.Line}");
        if (!documentsByPath.TryGetValue(violation.FilePath, out var document))
        {
            Console.Error.WriteLine($"  Warning: Could not find document for {violation.FilePath}");
            continue;
        }

        var root = await document.GetSyntaxRootAsync();
        var semanticModel = await document.GetSemanticModelAsync();
        if (root is null || semanticModel is null)
        {
            continue;
        }

        // Find the parameter at the reported location.
        var text = await document.GetTextAsync();
        var position = text.Lines[violation.Line - 1].Start + violation.Column - 1;
        var token = root.FindToken(position);
        var paramSyntax = token.Parent?.AncestorsAndSelf().OfType<ParameterSyntax>().FirstOrDefault();
        if (paramSyntax is null)
        {
            // Try finding by name in the enclosing parameter list.
            var node = root.FindNode(new TextSpan(position, violation.ParameterName.Length));
            paramSyntax = node?.AncestorsAndSelf().OfType<ParameterSyntax>().FirstOrDefault();
            if (paramSyntax is null)
            {
                Console.Error.WriteLine($"  Warning: Could not find parameter '{violation.ParameterName}' at {violation.FilePath}:{violation.Line}");
                continue;
            }
        }

        var paramSymbol = semanticModel.GetDeclaredSymbol(paramSyntax);
        if (paramSymbol is null)
        {
            continue;
        }

        var containingMethod = paramSymbol.ContainingSymbol as IMethodSymbol;
        if (containingMethod is null)
        {
            continue;
        }

        // Only process one parameter per method per iteration.
        // This avoids ordinal conflicts when multiple params are removed simultaneously.
        var methodKey = $"{containingMethod.ContainingType?.Name}.{containingMethod.Name}@{containingMethod.Locations.FirstOrDefault()?.SourceSpan.Start}";
        if (!processedMethods.Add(methodKey))
        {
            Console.WriteLine($"      Skipping (another param from same method already processed this iteration)");
            continue;
        }

        var ordinal = paramSymbol.Ordinal;

        // Record the parameter removal.
        AddParameterRemoval(editsByFile, violation.FilePath, paramSyntax);

        // If this is a test method with [InlineData] attributes, remove the
        // corresponding value from each attribute.
        RemoveInlineDataValues(editsByFile, violation.FilePath, paramSyntax, ordinal);

        // Find all call sites across the solution.
        try
        {
            Console.WriteLine($"      Finding references for {containingMethod.Name}...");
            var references = await SymbolFinder.FindReferencesAsync(containingMethod, solution);
            var totalLocations = references.Sum(r => r.Locations.Count());
            Console.WriteLine($"      Found {totalLocations} reference location(s).");

            // Track which files we've handled via SymbolFinder to avoid duplicates.
            var handledLocations = new HashSet<(string FilePath, int Start)>();

            foreach (var reference in references)
            {
                foreach (var location in reference.Locations)
                {
                    var refDocument = location.Document;
                    if (refDocument.FilePath is null)
                    {
                        continue;
                    }

                    var refRoot = await refDocument.GetSyntaxRootAsync();
                    if (refRoot is null)
                    {
                        continue;
                    }

                    var refNode = refRoot.FindNode(location.Location.SourceSpan);
                    handledLocations.Add((refDocument.FilePath, location.Location.SourceSpan.Start));

                    // Walk up to find the argument list.
                    var argList = FindArgumentList(refNode);
                    if (argList is null)
                    {
                        Console.WriteLine($"        MISS: No argument list found at {Path.GetFileName(refDocument.FilePath)}:{location.Location.SourceSpan} node={refNode.Kind()} parent={refNode.Parent?.Kind()}");
                        continue;
                    }

                    // Find the argument to remove.
                    var targetArg = FindArgumentForParameter(argList, violation.ParameterName, ordinal);
                    if (targetArg is null)
                    {
                        Console.WriteLine($"        MISS: Arg not found at ordinal {ordinal} in {argList.Arguments.Count}-arg list");
                        continue;
                    }

                    AddArgumentRemoval(editsByFile, refDocument.FilePath!, targetArg, argList);
                }
            }

            // (same-file fallback removed for diagnostics)
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  Warning: Failed to find references for {containingMethod.Name}: {ex.Message}");
        }
    }

    // Apply all edits.
    var filesModified = 0;
    foreach (var (filePath, spans) in editsByFile)
    {
        if (spans.Count == 0)
        {
            continue;
        }

        var sourceText = SourceText.From(File.ReadAllText(filePath));

        // Sort spans descending by start to apply from end to beginning.
        var sortedSpans = spans
            .Distinct()
            .OrderByDescending(s => s.Start)
            .ToList();

        // Check for overlapping spans and skip the smaller ones.
        var nonOverlapping = RemoveOverlappingSpans(sortedSpans);

        // Apply all changes at once — WithChanges handles position adjustments internally.
        var changes = nonOverlapping
            .OrderBy(s => s.Start)
            .Select(span => new TextChange(span, string.Empty))
            .ToList();
        sourceText = sourceText.WithChanges(changes);

        File.WriteAllText(filePath, sourceText.ToString());
        filesModified++;
    }

    return filesModified;
}

static void AddParameterRemoval(
    Dictionary<string, List<TextSpan>> editsByFile,
    string filePath,
    ParameterSyntax paramSyntax)
{
    if (!editsByFile.TryGetValue(filePath, out var spans))
    {
        spans = new List<TextSpan>();
        editsByFile[filePath] = spans;
    }

    if (paramSyntax.Parent is ParameterListSyntax paramList)
    {
        var index = paramList.Parameters.IndexOf(paramSyntax);
        spans.Add(ComputeSeparatedListRemovalSpan(paramList.Parameters, index));
    }
    else
    {
        spans.Add(paramSyntax.FullSpan);
    }
}

static void RemoveInlineDataValues(
    Dictionary<string, List<TextSpan>> editsByFile,
    string filePath,
    ParameterSyntax paramSyntax,
    int ordinal)
{
    // Find the containing method declaration.
    var methodDecl = paramSyntax.AncestorsAndSelf().OfType<MethodDeclarationSyntax>().FirstOrDefault();
    if (methodDecl is null)
    {
        return;
    }

    // Look for [InlineData(...)] attributes on this method.
    foreach (var attrList in methodDecl.AttributeLists)
    {
        foreach (var attr in attrList.Attributes)
        {
            var name = attr.Name.ToString();
            if (name is not ("InlineData" or "InlineDataAttribute"))
            {
                continue;
            }

            if (attr.ArgumentList is null || ordinal >= attr.ArgumentList.Arguments.Count)
            {
                continue;
            }

            if (!editsByFile.TryGetValue(filePath, out var spans))
            {
                spans = new List<TextSpan>();
                editsByFile[filePath] = spans;
            }

            spans.Add(ComputeSeparatedListRemovalSpan(attr.ArgumentList.Arguments, ordinal));
        }
    }
}

static void AddArgumentRemoval(
    Dictionary<string, List<TextSpan>> editsByFile,
    string filePath,
    ArgumentSyntax argSyntax,
    ArgumentListSyntax argList)
{
    if (!editsByFile.TryGetValue(filePath, out var spans))
    {
        spans = new List<TextSpan>();
        editsByFile[filePath] = spans;
    }

    var index = argList.Arguments.IndexOf(argSyntax);
    spans.Add(ComputeSeparatedListRemovalSpan(argList.Arguments, index));
}

static ArgumentListSyntax? FindArgumentList(SyntaxNode node)
{
    // Walk ancestors to find an invocation or object creation.
    foreach (var ancestor in node.AncestorsAndSelf())
    {
        if (ancestor is InvocationExpressionSyntax invocation)
        {
            // Skip nameof() — it's syntactically an invocation but not a real call.
            if (invocation.Expression is IdentifierNameSyntax id &&
                id.Identifier.ValueText == "nameof")
            {
                return null;
            }

            return invocation.ArgumentList;
        }

        if (ancestor is ObjectCreationExpressionSyntax objectCreation)
        {
            return objectCreation.ArgumentList;
        }

        if (ancestor is ImplicitObjectCreationExpressionSyntax implicitCreation)
        {
            return implicitCreation.ArgumentList;
        }

        if (ancestor is ConstructorInitializerSyntax ctorInit)
        {
            return ctorInit.ArgumentList;
        }
    }

    return null;
}

static ArgumentSyntax? FindArgumentForParameter(
    ArgumentListSyntax argList, string paramName, int ordinal)
{
    // First check for named arguments matching the parameter name.
    foreach (var arg in argList.Arguments)
    {
        if (arg.NameColon is not null &&
            arg.NameColon.Name.Identifier.ValueText == paramName)
        {
            return arg;
        }
    }

    // Fall back to positional.
    if (ordinal < argList.Arguments.Count)
    {
        return argList.Arguments[ordinal];
    }

    return null;
}

static TextSpan ComputeSeparatedListRemovalSpan<T>(
    SeparatedSyntaxList<T> list, int index) where T : SyntaxNode
{
    var node = list[index];

    if (list.Count == 1)
    {
        // Only item — just remove the node itself (keep the parens).
        return node.FullSpan;
    }

    if (index < list.Count - 1)
    {
        // Not the last item — remove node + trailing comma + whitespace after comma.
        var separator = list.GetSeparator(index);
        var end = separator.Span.End;

        // Consume whitespace after the comma.
        var sourceText = node.SyntaxTree?.GetText();
        if (sourceText is not null)
        {
            while (end < sourceText.Length && sourceText[end] == ' ')
            {
                end++;
            }
        }

        return TextSpan.FromBounds(node.FullSpan.Start, end);
    }
    else
    {
        // Last item — remove preceding comma + whitespace + node.
        var separator = list.GetSeparator(index - 1);
        return TextSpan.FromBounds(separator.SpanStart, node.FullSpan.End);
    }
}

static List<TextSpan> RemoveOverlappingSpans(List<TextSpan> sortedDescending)
{
    var result = new List<TextSpan>();

    foreach (var span in sortedDescending)
    {
        // Check if this span overlaps with any already-accepted span.
        var overlaps = false;
        foreach (var accepted in result)
        {
            if (span.IntersectsWith(accepted))
            {
                overlaps = true;
                break;
            }
        }

        if (!overlaps)
        {
            result.Add(span);
        }
    }

    return result;
}

static List<string> DiscoverProjectPaths(string solutionPath)
{
    var solutionDir = Path.GetDirectoryName(solutionPath)!;
    var results = new List<string>();

    // Parse the .slnx XML to extract project paths.
    var content = File.ReadAllText(solutionPath);
    var projectRegex = new Regex(@"Path=""([^""]+\.csproj)""");
    foreach (Match match in projectRegex.Matches(content))
    {
        var relativePath = match.Groups[1].Value;
        var fullPath = Path.GetFullPath(Path.Combine(solutionDir, relativePath));
        if (File.Exists(fullPath))
        {
            results.Add(fullPath);
        }
    }

    return results;
}

// ---------------------------------------------------------------------------
// Post-pass: run `dotnet build` and fix CS1501 errors by removing extra args.
// ---------------------------------------------------------------------------
static List<BuildError> RunBuildAndCollectErrors(string solutionPath)
{
    var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
    var psi = new ProcessStartInfo
    {
        FileName = "dotnet",
        Arguments = $"build \"{solutionPath}\"",
        WorkingDirectory = solutionDir,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    using var process = Process.Start(psi)!;
    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    process.WaitForExit();
    var stdout = stdoutTask.GetAwaiter().GetResult();
    var stderr = stderrTask.GetAwaiter().GetResult();

    var combined = stdout + "\n" + stderr;
    var results = new List<BuildError>();

    // Match any build error — not just CS1501.
    // Pattern: file(line,col): error XXXX: message
    var regex = new Regex(
        @"^(.+?)\((\d+),(\d+)\): error (\w+):",
        RegexOptions.Multiline);

    foreach (Match match in regex.Matches(combined))
    {
        results.Add(new BuildError(
            FilePath: match.Groups[1].Value.Trim(),
            Line: int.Parse(match.Groups[2].Value),
            Column: int.Parse(match.Groups[3].Value),
            MethodName: match.Groups[4].Value, // actually the error code now
            ArgumentCount: 0));
    }

    // Deduplicate
    return results.DistinctBy(e => (e.FilePath, e.Line, e.Column)).ToList();
}

static int FixArgumentCountErrors(List<BuildError> errors)
{
    // For each error, we know the call site has too many arguments.
    // We need to figure out which argument to remove. The method declaration
    // (already edited) tells us the current parameter count. The call site
    // has more args than the method accepts.
    //
    // Strategy: parse the file, find the invocation at the error location,
    // then find the method declaration to determine which parameter was removed
    // (by comparing the current param list with the arg list).

    var editsByFile = new Dictionary<string, List<TextSpan>>(StringComparer.OrdinalIgnoreCase);

    foreach (var error in errors)
    {
        if (!File.Exists(error.FilePath))
        {
            continue;
        }

        var sourceText = File.ReadAllText(error.FilePath);
        var tree = CSharpSyntaxTree.ParseText(sourceText, path: error.FilePath);
        var root = tree.GetRoot();
        var text = tree.GetText();

        // Find the node at the error location.
        var position = text.Lines[error.Line - 1].Start + error.Column - 1;
        var token = root.FindToken(position);
        var node = token.Parent;

        // Find the enclosing invocation.
        var invocation = node?.AncestorsAndSelf().OfType<InvocationExpressionSyntax>().FirstOrDefault();
        if (invocation is null)
        {
            continue;
        }

        var argList = invocation.ArgumentList;
        var callerArgCount = argList.Arguments.Count;

        // The method now has fewer params. We need to find the declaration to know
        // which params remain. Search by method name in all source files.
        var methodName = error.MethodName;

        // Find the declaration of this method — search all .cs files in Source/
        var declaration = FindMethodDeclaration(methodName, error.FilePath, callerArgCount);
        if (declaration is null)
        {
            // Can't find the declaration — try removing from the end as a heuristic.
            // The most recently removed param is usually near the end.
            continue;
        }

        // Compare: declaration has N params, call site has N+K args.
        // Find which positional args don't match the declaration params by name.
        var declParamNames = declaration.Value.ParamNames;
        var argsToRemove = FindMismatchedArguments(argList, declParamNames);

        if (!editsByFile.TryGetValue(error.FilePath, out var spans))
        {
            spans = new List<TextSpan>();
            editsByFile[error.FilePath] = spans;
        }

        foreach (var argIndex in argsToRemove.OrderByDescending(i => i))
        {
            spans.Add(ComputeSeparatedListRemovalSpan(argList.Arguments, argIndex));
        }
    }

    // Apply edits.
    var filesFixed = 0;
    foreach (var (filePath, spans) in editsByFile)
    {
        if (spans.Count == 0)
        {
            continue;
        }

        var sourceText = SourceText.From(File.ReadAllText(filePath));
        var nonOverlapping = RemoveOverlappingSpans(
            spans.Distinct().OrderByDescending(s => s.Start).ToList());

        var changes = nonOverlapping
            .OrderBy(s => s.Start)
            .Select(span => new TextChange(span, string.Empty))
            .ToList();
        sourceText = sourceText.WithChanges(changes);

        File.WriteAllText(filePath, sourceText.ToString());
        filesFixed++;
    }

    return filesFixed;
}

static (List<string> ParamNames, string FilePath)? FindMethodDeclaration(
    string methodName, string callerFilePath, int callerArgCount)
{
    // First check the same file as the caller.
    var filesToCheck = new List<string> { callerFilePath };

    // Also check other source files in the same directory and parent.
    var dir = Path.GetDirectoryName(callerFilePath);
    if (dir is not null)
    {
        filesToCheck.AddRange(Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories));
    }

    foreach (var file in filesToCheck.Distinct())
    {
        if (!File.Exists(file))
        {
            continue;
        }

        var text = File.ReadAllText(file);
        var tree = CSharpSyntaxTree.ParseText(text, path: file);
        var root = tree.GetRoot();

        var methods = root.DescendantNodes().OfType<MethodDeclarationSyntax>()
            .Where(m => m.Identifier.ValueText == methodName);

        foreach (var method in methods)
        {
            var paramCount = method.ParameterList.Parameters.Count;
            // The declaration should have fewer params than the caller's arg count.
            if (paramCount < callerArgCount)
            {
                var paramNames = method.ParameterList.Parameters
                    .Select(p => p.Identifier.ValueText)
                    .ToList();
                return (paramNames, file);
            }
        }
    }

    return null;
}

static List<int> FindMismatchedArguments(ArgumentListSyntax argList, List<string> declParamNames)
{
    // Simple approach: the declaration has N params, the call has M args where M > N.
    // Remove args from the end that exceed the param count (most common case),
    // unless there are named arguments we can match.
    var argsToRemove = new List<int>();
    var declCount = declParamNames.Count;
    var callCount = argList.Arguments.Count;

    if (callCount <= declCount)
    {
        return argsToRemove;
    }

    // Remove extra args starting from the end.
    // More sophisticated: try to match named args and remove unmatched positional ones.
    for (var i = callCount - 1; i >= declCount; i--)
    {
        argsToRemove.Add(i);
    }

    return argsToRemove;
}

// ---------------------------------------------------------------------------
// Data types
// ---------------------------------------------------------------------------

record FormatViolation(string FilePath, int Line, int Column, string ParameterName);

record BuildError(string FilePath, int Line, int Column, string MethodName, int ArgumentCount);
