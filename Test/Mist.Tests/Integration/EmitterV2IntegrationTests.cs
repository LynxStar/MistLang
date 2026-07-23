using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Integration;

/// <summary>
/// End-to-end tests for the v2 C# emitter. Compiles Mist source through the
/// ProgramModel, emits C# via <see cref="CSharpEmitterV2"/>, compiles with
/// Roslyn in-process, runs the output DLL, and asserts on stdout.
/// </summary>
public sealed class EmitterV2IntegrationTests : IDisposable
{
    private readonly string outputDir;
    private readonly string binDir;

    public EmitterV2IntegrationTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistEmitV2_" + Guid.NewGuid().ToString("N"));
        binDir = Path.Combine(outputDir, "bin");
        _ = Directory.CreateDirectory(outputDir);
        _ = Directory.CreateDirectory(binDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(outputDir))
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SimpliestSample_EmitsAndRunsCorrectly()
    {
        var source = """
            using Standard;
            namespace Simpliest;
            public class Program
            {
                public static void Main(string[] args)
                {
                    RunDemo();
                }
                public static void RunDemo()
                {
                    Console.WriteLine("General KENOBI!");
                }
            }
            """;

        var result = CompileAndRun(source, "Simpliest");

        Assert.True(result.CompileSucceeded, $"Compilation failed:\n{result.CompileDiagnostics}");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("General KENOBI!", result.Stdout.Trim());
    }


    [Fact]
    public void PrimitivesSample_SingleFile_EmitsAndRunsCorrectly()
    {
        var source = """
            using Standard;

            namespace Primitives;

            public class Program
            {
                private static int Fibonacci(int n)
                {
                    if (n <= 0)
                    {
                        return 0;
                    }
                    if (n == 1)
                    {
                        return 1;
                    }
                    return Fibonacci(n - 1) + Fibonacci(n - 2);
                }

                private static int FindFirst(int[] values, int target)
                {
                    int index = 0;
                    foreach (int v in values)
                    {
                        if (v == target)
                        {
                            return index;
                        }
                        index = index + 1;
                    }
                    return -1;
                }

                private static int DigitSum(int n)
                {
                    int sum = 0;
                    if (n < 0)
                    {
                        n = -n;
                    }
                    while (n > 0)
                    {
                        sum = sum + n % 10;
                        n = n / 10;
                    }
                    return sum;
                }

                public static void Main(string[] args)
                {
                    RunDemo();
                }

                public static void RunDemo()
                {
                    int i = 42;
                    uint ui = 100u;
                    long l = 100L;
                    ulong ul = 999UL;
                    short s = 16;
                    ushort us = 32;
                    byte b = 255;
                    sbyte sb = -128;
                    float f = 2.5f;
                    double d = 3.14;
                    decimal m = 99.99m;
                    bool flag = true;
                    char ch = 'Z';
                    int hex = 0xFF;
                    Console.WriteLine($"int={i} uint={ui} long={l} ulong={ul}");
                    Console.WriteLine($"short={s} ushort={us} byte={b} sbyte={sb}");
                    Console.WriteLine($"float={f} double={d} decimal={m}");
                    Console.WriteLine($"bool={flag} char={ch} hex={hex}");

                    uint bigUint = 3000000000u;
                    long bigLong = 5000000000L;
                    ulong bigUlong = 5000000000UL;
                    Console.WriteLine($"bigUint={bigUint} bigLong={bigLong} bigUlong={bigUlong}");

                    string greeting = "Hello, Mist!";
                    string interp = $"The answer is {i}";
                    string interpExpr = $"Hex doubled: {hex * 2}";
                    Console.WriteLine(greeting);
                    Console.WriteLine(interp);
                    Console.WriteLine(interpExpr);

                    var inferred = 123;
                    var inferredStr = "inferred";
                    var inferredBool = false;
                    Console.WriteLine($"var: {inferred} {inferredStr} {inferredBool}");

                    int a = 17;
                    int bv = 5;
                    Console.WriteLine($"{a}+{bv}={a + bv} {a}-{bv}={a - bv} {a}*{bv}={a * bv}");
                    Console.WriteLine($"{a}/{bv}={a / bv} {a}%{bv}={a % bv} neg={-a}");

                    Console.WriteLine($"=={a == bv} !={a != bv} <{a < bv} >{a > bv}");
                    Console.WriteLine($"<={a <= bv} >={a >= bv}");

                    bool t = true;
                    bool fv = false;
                    Console.WriteLine($"&&={t && fv} ||={t || fv} !={!t}");

                    int parenResult = (a + bv) * (a - bv);
                    Console.WriteLine($"({a}+{bv})*({a}-{bv})={parenResult}");

                    int score = 85;
                    if (score >= 90)
                    {
                        Console.WriteLine("Grade A");
                    }
                    else if (score >= 80)
                    {
                        Console.WriteLine("Grade B");
                    }
                    else
                    {
                        Console.WriteLine("Grade F");
                    }

                    int countdown = 5;
                    while (countdown > 0)
                    {
                        Console.Write($"{countdown} ");
                        countdown = countdown - 1;
                    }
                    Console.WriteLine("Liftoff!");

                    int forSum = 0;
                    for (int j = 1; j <= 10; j = j + 1)
                    {
                        forSum = forSum + j;
                    }
                    Console.WriteLine($"Sum 1..10 = {forSum}");

                    var nums = new int[6];
                    for (int j = 0; j < 6; j = j + 1)
                    {
                        nums[j] = (j + 1) * (j + 1);
                    }
                    Console.Write("Squares: ");
                    foreach (int n in nums)
                    {
                        Console.Write($"{n} ");
                    }
                    Console.WriteLine("");

                    for (int j = 0; j < 100; j = j + 1)
                    {
                        if (j == 5)
                        {
                            Console.WriteLine($"Break at j={j}");
                            break;
                        }
                    }

                    Console.Write("Fib: ");
                    for (int j = 0; j < 10; j = j + 1)
                    {
                        Console.Write($"{Fibonacci(j)} ");
                    }
                    Console.WriteLine("");

                    var haystack = new int[5];
                    haystack[0] = 10;
                    haystack[1] = 20;
                    haystack[2] = 30;
                    haystack[3] = 40;
                    haystack[4] = 50;
                    Console.WriteLine($"FindFirst(30)={FindFirst(haystack, 30)}");
                    Console.WriteLine($"FindFirst(99)={FindFirst(haystack, 99)}");

                    Console.WriteLine($"DigitSum(12345)={DigitSum(12345)}");

                    string strA = "Mist";
                    string strB = "Lang";
                    string combined = strA + " " + strB;
                    Console.WriteLine($"concat={combined}");

                    for (int row = 1; row <= 3; row = row + 1)
                    {
                        for (int col = 1; col <= 3; col = col + 1)
                        {
                            Console.Write($"{row * col} ");
                        }
                        Console.WriteLine("");
                    }
                }
            }
            """;

        var result = CompileAndRun(source, "Primitives");

        Assert.True(result.CompileSucceeded, $"Compilation failed:\n{result.CompileDiagnostics}");
        Assert.Equal(0, result.ExitCode);

        var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        // Verify key output lines
        Assert.Equal("int=42 uint=100 long=100 ulong=999", lines[0]);
        Assert.Equal("short=16 ushort=32 byte=255 sbyte=-128", lines[1]);
        Assert.Contains("float=2.5", lines[2]);
        Assert.Contains("double=3.14", lines[2]);
        Assert.Contains("decimal=99.99", lines[2]);
        Assert.Equal("bool=True char=Z hex=255", lines[3]);
        Assert.Equal("bigUint=3000000000 bigLong=5000000000 bigUlong=5000000000", lines[4]);
        Assert.Equal("Hello, Mist!", lines[5]);
        Assert.Equal("The answer is 42", lines[6]);
        Assert.Equal("Hex doubled: 510", lines[7]);
        Assert.Equal("var: 123 inferred False", lines[8]);
        Assert.Equal("17+5=22 17-5=12 17*5=85", lines[9]);
        Assert.Equal("17/5=3 17%5=2 neg=-17", lines[10]);
        Assert.Equal("==False !=True <False >True", lines[11]);
        Assert.Equal("<=False >=True", lines[12]);
        Assert.Equal("&&=False ||=True !=False", lines[13]);
        Assert.Equal("(17+5)*(17-5)=264", lines[14]);
        Assert.Equal("Grade B", lines[15]);
        Assert.Equal("5 4 3 2 1 Liftoff!", lines[16]);
        Assert.Equal("Sum 1..10 = 55", lines[17]);
        Assert.Equal("Squares: 1 4 9 16 25 36 ", lines[18]);
        Assert.Equal("Break at j=5", lines[19]);
        Assert.Equal("Fib: 0 1 1 2 3 5 8 13 21 34 ", lines[20]);
        Assert.Equal("FindFirst(30)=2", lines[21]);
        Assert.Equal("FindFirst(99)=-1", lines[22]);
        Assert.Equal("DigitSum(12345)=15", lines[23]);
        Assert.Equal("concat=Mist Lang", lines[24]);
        Assert.Equal("1 2 3 ", lines[25]);
        Assert.Equal("2 4 6 ", lines[26]);
        Assert.Equal("3 6 9 ", lines[27]);
    }

    [Fact]
    public void PrimitivesSample_MultiFile_EmitsAndRunsCorrectly()
    {
        var programSource = """
            using Standard;

            namespace Primitives;

            public class Program
            {
                private static int Fibonacci(int n)
                {
                    if (n <= 0)
                    {
                        return 0;
                    }
                    if (n == 1)
                    {
                        return 1;
                    }
                    return Fibonacci(n - 1) + Fibonacci(n - 2);
                }

                private static int FindFirst(int[] values, int target)
                {
                    int index = 0;
                    foreach (int v in values)
                    {
                        if (v == target)
                        {
                            return index;
                        }
                        index = index + 1;
                    }
                    return -1;
                }

                private static int DigitSum(int n)
                {
                    int sum = 0;
                    if (n < 0)
                    {
                        n = -n;
                    }
                    while (n > 0)
                    {
                        sum = sum + n % 10;
                        n = n / 10;
                    }
                    return sum;
                }

                public static void Main(string[] args)
                {
                    RunDemo();
                }

                public static void RunDemo()
                {
                    int i = 42;
                    uint ui = 100u;
                    long l = 100L;
                    ulong ul = 999UL;
                    short s = 16;
                    ushort us = 32;
                    byte b = 255;
                    sbyte sb = -128;
                    float f = 2.5f;
                    double d = 3.14;
                    decimal m = 99.99m;
                    bool flag = true;
                    char ch = 'Z';
                    int hex = 0xFF;
                    Console.WriteLine($"int={i} uint={ui} long={l} ulong={ul}");
                    Console.WriteLine($"short={s} ushort={us} byte={b} sbyte={sb}");
                    Console.WriteLine($"float={f} double={d} decimal={m}");
                    Console.WriteLine($"bool={flag} char={ch} hex={hex}");

                    uint bigUint = 3000000000u;
                    long bigLong = 5000000000L;
                    ulong bigUlong = 5000000000UL;
                    Console.WriteLine($"bigUint={bigUint} bigLong={bigLong} bigUlong={bigUlong}");

                    string greeting = "Hello, Mist!";
                    string interp = $"The answer is {i}";
                    string interpExpr = $"Hex doubled: {hex * 2}";
                    Console.WriteLine(greeting);
                    Console.WriteLine(interp);
                    Console.WriteLine(interpExpr);

                    var inferred = 123;
                    var inferredStr = "inferred";
                    var inferredBool = false;
                    Console.WriteLine($"var: {inferred} {inferredStr} {inferredBool}");

                    int a = 17;
                    int bv = 5;
                    Console.WriteLine($"{a}+{bv}={a + bv} {a}-{bv}={a - bv} {a}*{bv}={a * bv}");
                    Console.WriteLine($"{a}/{bv}={a / bv} {a}%{bv}={a % bv} neg={-a}");

                    Console.WriteLine($"=={a == bv} !={a != bv} <{a < bv} >{a > bv}");
                    Console.WriteLine($"<={a <= bv} >={a >= bv}");

                    bool t = true;
                    bool fv = false;
                    Console.WriteLine($"&&={t && fv} ||={t || fv} !={!t}");

                    int parenResult = (a + bv) * (a - bv);
                    Console.WriteLine($"({a}+{bv})*({a}-{bv})={parenResult}");

                    int score = 85;
                    if (score >= 90)
                    {
                        Console.WriteLine("Grade A");
                    }
                    else if (score >= 80)
                    {
                        Console.WriteLine("Grade B");
                    }
                    else
                    {
                        Console.WriteLine("Grade F");
                    }

                    int countdown = 5;
                    while (countdown > 0)
                    {
                        Console.Write($"{countdown} ");
                        countdown = countdown - 1;
                    }
                    Console.WriteLine("Liftoff!");

                    int forSum = 0;
                    for (int j = 1; j <= 10; j = j + 1)
                    {
                        forSum = forSum + j;
                    }
                    Console.WriteLine($"Sum 1..10 = {forSum}");

                    var nums = new int[6];
                    for (int j = 0; j < 6; j = j + 1)
                    {
                        nums[j] = (j + 1) * (j + 1);
                    }
                    Console.Write("Squares: ");
                    foreach (int n in nums)
                    {
                        Console.Write($"{n} ");
                    }
                    Console.WriteLine("");

                    for (int j = 0; j < 100; j = j + 1)
                    {
                        if (j == 5)
                        {
                            Console.WriteLine($"Break at j={j}");
                            break;
                        }
                    }

                    Console.Write("Fib: ");
                    for (int j = 0; j < 10; j = j + 1)
                    {
                        Console.Write($"{Fibonacci(j)} ");
                    }
                    Console.WriteLine("");

                    var haystack = new int[5];
                    haystack[0] = 10;
                    haystack[1] = 20;
                    haystack[2] = 30;
                    haystack[3] = 40;
                    haystack[4] = 50;
                    Console.WriteLine($"FindFirst(30)={FindFirst(haystack, 30)}");
                    Console.WriteLine($"FindFirst(99)={FindFirst(haystack, 99)}");

                    Console.WriteLine($"DigitSum(12345)={DigitSum(12345)}");

                    string strA = "Mist";
                    string strB = "Lang";
                    string combined = strA + " " + strB;
                    Console.WriteLine($"concat={combined}");

                    for (int row = 1; row <= 3; row = row + 1)
                    {
                        for (int col = 1; col <= 3; col = col + 1)
                        {
                            Console.Write($"{row * col} ");
                        }
                        Console.WriteLine("");
                    }

                    Console.WriteLine($"isGreeting={StringUtils.IsGreeting("Hello world")}");
                    Console.WriteLine($"bracket={StringUtils.Bracket("Mist")}");
                    Console.WriteLine($"doubled={StringUtils.DoubleNumeric("21")}");
                    Console.WriteLine($"length={StringUtils.GetLength("Hello")}");
                }
            }
            """;

        var stringUtilsSource = """
            using Standard;

            namespace Primitives;

            public class StringUtils
            {
                public static bool IsGreeting(string input)
                {
                    if (input.Contains("Hello") || input.Contains("Hi"))
                    {
                        return true;
                    }
                    return false;
                }

                public static string Bracket(string input)
                {
                    return "[" + input + "]";
                }

                public static string DoubleNumeric(string numStr)
                {
                    int value = Convert.ToInt32(numStr);
                    int doubled = value * 2;
                    return $"{doubled}";
                }

                public static int GetLength(string input)
                {
                    return input.Length;
                }
            }
            """;

        var result = CompileAndRun(
            [new SourceFile("Program.mist", programSource), new SourceFile("StringUtils.mist", stringUtilsSource)],
            "Primitives");

        Assert.True(result.CompileSucceeded, $"Compilation failed:\n{result.CompileDiagnostics}");
        Assert.Equal(0, result.ExitCode);

        var lines = result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.TrimEnd('\r'))
            .ToArray();

        // Verify StringUtils output (lines after the single-file content)
        Assert.Equal("isGreeting=True", lines[28]);
        Assert.Equal("bracket=[Mist]", lines[29]);
        Assert.Equal("doubled=42", lines[30]);
        Assert.Equal("length=5", lines[31]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Harness
    // ═══════════════════════════════════════════════════════════════════════

    private record RunResult(
        bool CompileSucceeded,
        string CompileDiagnostics,
        int ExitCode,
        string Stdout,
        string Stderr);

    private record SourceFile(string FileName, string Content);

    private RunResult CompileAndRun(string source, string projectName)
    {
        return CompileAndRun([new SourceFile("Program.mist", source)], projectName);
    }

    private RunResult CompileAndRun(SourceFile[] sources, string projectName)
    {
        // Stage 1-3: Lex & Parse each source file
        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseDiagnostics = new DiagnosticBag();

        var units = new List<CompilationUnit>();
        var asts = new List<Storm.Runtime.SyntaxNode>();

        foreach(var file in sources)
        {
            var sourceText = new SourceText(file.FileName, file.Content);
            var tokens = lexer.Lex(sourceText, parseDiagnostics);
            var parseResult = parser.Parse(tokens, parseDiagnostics);

            Assert.NotNull(parseResult.Root);

            units.Add(new CompilationUnit(sourceText));
            asts.Add(parseResult.Root);
        }

        // Stage 4: Build ProgramModel
        var compilation = new MistCompilation(ImmutableArray.CreateRange(units));

        var sourcePaths = sources.Select(s => s.FileName).ToImmutableArray();
        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: sourcePaths,
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: binDir,
            ProjectName: projectName,
            NamespaceRoot: projectName,
            EnabledAnalyzerIds: ImmutableArray<string>.Empty);

        var buildDiagnostics = new DiagnosticBag();
        var programModel = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.CreateRange(asts),
            options,
            buildDiagnostics,
            new StandardTypesAmbientProvider());

        Assert.NotNull(programModel);

        // Stage 6: Emit via v2 emitter
        var emitDiagnostics = new DiagnosticBag();
        var emitter = new CSharpEmitterV2(programModel!, options, emitDiagnostics);
        var compilerResult = emitter.Emit();

        if(!compilerResult.Succeeded)
        {
            var diagText = string.Join("\n", compilerResult.Diagnostics.Select(d => $"  {d.Id.Value}: {d.Message}"));
            return new RunResult(false, diagText, -1, "", "");
        }

        // Run the compiled output
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = compilerResult.PrimaryOutputArtifactPath!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        Assert.NotNull(process);

        var stdout = process!.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();

        Assert.True(process.WaitForExit(30_000), "Process timed out after 30 seconds");

        return new RunResult(true, "", process.ExitCode, stdout, stderr);
    }

    /// <summary>
    /// Ambient provider that registers Standard Library type shells with Console.WriteLine
    /// and Console.Write methods.
    /// </summary>
    private sealed class StandardTypesAmbientProvider : IAmbientTypeProvider
    {
        public void BuildAndRegister(ProgramModelClass programModel)
        {
            AmbientProviderTestHelper.RegisterStandardTypes(programModel);

            // Register Console with static WriteLine(string) and Write(string) methods.
            var consoleScope = new Scope(null);
            var consoleType = new ClassType("Console", "Standard", consoleScope, null, null!);
            _ = programModel.RegisterType(consoleType);

            var voidType = programModel.TypesByFQN["Standard.Void"];
            var stringType = programModel.TypesByFQN["Standard.String"];
            var writeLineMethod = new Method(
                "WriteLine",
                voidType,
                [new Parameter("value", stringType)],
                new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public });
            consoleType.MethodsList.Add(writeLineMethod);
            consoleType.Scope.AddMethod(writeLineMethod);

            var writeMethod = new Method(
                "Write",
                voidType,
                [new Parameter("value", stringType)],
                new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public });
            consoleType.MethodsList.Add(writeMethod);
            consoleType.Scope.AddMethod(writeMethod);

            // Enrich String with instance members needed by StringUtils.
            var boolType = programModel.TypesByFQN["Standard.Boolean"];
            var intType = programModel.TypesByFQN["Standard.Int32"];
            if(stringType is MemberType stringMemberType)
            {
                var containsMethod = new Method(
                    "Contains",
                    boolType,
                    [new Parameter("value", stringType)],
                    new MemberModifiers { Accessibility = Accessibility.Public });
                stringMemberType.MethodsList.Add(containsMethod);
                stringMemberType.Scope.AddMethod(containsMethod);

                var lengthProperty = new Property(
                    "Length",
                    intType,
                    new MemberModifiers { Accessibility = Accessibility.Public });
                stringMemberType.PropertiesList.Add(lengthProperty);
            }

            // Register Convert with static ToInt32(string) method.
            var convertScope = new Scope(null);
            var convertType = new ClassType("Convert", "Standard", convertScope, null, null!);
            _ = programModel.RegisterType(convertType);

            var toInt32Method = new Method(
                "ToInt32",
                intType,
                [new Parameter("value", stringType)],
                new MemberModifiers { IsStatic = true, Accessibility = Accessibility.Public });
            convertType.MethodsList.Add(toInt32Method);
            convertType.Scope.AddMethod(toInt32Method);

            // Register an indexer on the Array<,> definition so that receiver[index]
            // resolves the element type through the indexer. The indexer's return type is
            // the type parameter T (ordinal 0), which ConstructedType.Substitute resolves
            // to the concrete element type (e.g., Int32 for int[]).
            if(programModel.TypesByFQN.TryGetValue("Standard.Array<,>", out var arrayDef)
                && arrayDef is MemberType arrayMemberType)
            {
                var elementParam = new TypeParameter("T", 0, new Scope(null), null);
                arrayMemberType.TypeParametersList.Add(elementParam);
                var indexer = new Indexer(
                    elementParam,
                    [new Parameter("index", intType)],
                    new MemberModifiers { Accessibility = Accessibility.Public });
                arrayMemberType.IndexersList.Add(indexer);
            }

            foreach(var type in programModel.TypesByFQN.Values)
            {
                if(type is MistType { NamespaceName: "Standard" } standardType)
                {
                    standardType.Origin = TypeOrigin.Ambient;
                }
            }
        }
    }
}
