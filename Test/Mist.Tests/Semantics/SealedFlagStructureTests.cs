using System.Collections.Immutable;
using Mist.Analysis.Builtin;
using Mist.Analysis.Contracts;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Tests.Semantics;

/// <summary>
/// Example tests for the sealed flag structure on ClassType and StructType.
///
/// Asserts that <c>sealed</c> vs non-<c>sealed</c> class/struct sets <c>IsSealed</c>
/// independently of its <c>Accessibility</c> value.
///
/// Requirements: 3.2
/// </summary>
public sealed class SealedFlagStructureTests : IDisposable
{
    private readonly string scratchDir;

    public SealedFlagStructureTests()
    {
        scratchDir = Path.Combine(
            Path.GetTempPath(),
            "mist-sealed-flag-" + Guid.NewGuid().ToString("N"));
        _ = Directory.CreateDirectory(scratchDir);
    }

    public void Dispose()
    {
        if(Directory.Exists(scratchDir))
        {
            try { Directory.Delete(scratchDir, recursive: true); } catch { /* best effort */ }
        }

        GC.SuppressFinalize(this);
    }

    // ─── Infrastructure ──────────────────────────────────────────────────

    private sealed class CapturingSemanticModelBuilder : ISemanticModelBuilder
    {
        private readonly SemanticModelBuilder inner = new();

        public ISemanticModel? CapturedModel { get; private set; }

        public ISemanticModel Build(
            MistCompilation compilation,
            ImmutableArray<Storm.Runtime.SyntaxNode> asts,
            IAmbientTypeResolver? ambientTypeResolver,
            string namespaceRoot,
            string projectFolder,
            DiagnosticBag diagnostics,
            ImmutableHashSet<string>? directReferencePaths = null)
        {
            var model = inner.Build(compilation, asts, ambientTypeResolver, namespaceRoot, projectFolder, diagnostics, directReferencePaths);
            CapturedModel = model;
            return model;
        }
    }

    private static CompilerDriver CreateDriverWithCapture(CapturingSemanticModelBuilder capture)
    {
        return new CompilerDriver(
            new Mist.Lexing.MistLexer(),
            new Mist.Parsing.MistParser(),
            capture,
            ImmutableArray.Create<IAnalyzer>(new TypeCheckerAnalyzer()),
            ImmutableArray.Create<IBackend>(new CSharpTranspilerBackend()));
    }

    private CompilerOptions MakeOptions(string filePath)
    {
        return new CompilerOptions(
            ProjectFolder: scratchDir,
            SourcePaths: ImmutableArray.Create(filePath),
            Backend: new BackendId("csharp"),
            OutputDirectory: scratchDir,
            BinDirectory: Path.Combine(scratchDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            EmitToDisk: false);
    }

    // ─── Class tests ─────────────────────────────────────────────────────

    [Fact]
    public void SealedPublicClass_HasIsSealedTrue_AndAccessibilityPublic()
    {
        var source = """
            public sealed class Locked { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        _ = driver.Compile(MakeOptions(filePath));

        Assert.NotNull(capture.CapturedModel);

        var symbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "Locked" && s.Kind == SymbolKind.Class);
        Assert.NotNull(symbol);

        var classType = Assert.IsType<ClassType>(symbol!.DeclaredType);
        Assert.True(classType.IsSealed);
        Assert.Equal(Accessibility.Public, classType.Accessibility);
    }

    [Fact]
    public void NonSealedPublicClass_HasIsSealedFalse_AndAccessibilityPublic()
    {
        var source = """
            public class Open { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        _ = driver.Compile(MakeOptions(filePath));

        Assert.NotNull(capture.CapturedModel);

        var symbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "Open" && s.Kind == SymbolKind.Class);
        Assert.NotNull(symbol);

        var classType = Assert.IsType<ClassType>(symbol!.DeclaredType);
        Assert.False(classType.IsSealed);
        Assert.Equal(Accessibility.Public, classType.Accessibility);
    }

    [Fact]
    public void SealedInternalClass_HasIsSealedTrue_AndAccessibilityInternal()
    {
        var source = """
            internal sealed class LockedInternal { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        _ = driver.Compile(MakeOptions(filePath));

        Assert.NotNull(capture.CapturedModel);

        var symbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "LockedInternal" && s.Kind == SymbolKind.Class);
        Assert.NotNull(symbol);

        var classType = Assert.IsType<ClassType>(symbol!.DeclaredType);
        Assert.True(classType.IsSealed);
        Assert.Equal(Accessibility.Internal, classType.Accessibility);
    }

    [Fact]
    public void NonSealedInternalClass_HasIsSealedFalse_AndAccessibilityInternal()
    {
        var source = """
            internal class OpenInternal { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        _ = driver.Compile(MakeOptions(filePath));

        Assert.NotNull(capture.CapturedModel);

        var symbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "OpenInternal" && s.Kind == SymbolKind.Class);
        Assert.NotNull(symbol);

        var classType = Assert.IsType<ClassType>(symbol!.DeclaredType);
        Assert.False(classType.IsSealed);
        Assert.Equal(Accessibility.Internal, classType.Accessibility);
    }

    [Fact]
    public void SealedPrivateClass_HasIsSealedTrue_AndAccessibilityPrivate()
    {
        // A private sealed class nested inside a public class
        var source = """
            public class Outer
            {
                private sealed class Inner { }
            }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        _ = driver.Compile(MakeOptions(filePath));

        Assert.NotNull(capture.CapturedModel);

        var symbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "Inner" && s.Kind == SymbolKind.Class);
        Assert.NotNull(symbol);

        var classType = Assert.IsType<ClassType>(symbol!.DeclaredType);
        Assert.True(classType.IsSealed);
        Assert.Equal(Accessibility.Private, classType.Accessibility);
    }

    // ─── Struct tests ────────────────────────────────────────────────────

    [Fact]
    public void SealedPublicStruct_HasIsSealedTrue_AndAccessibilityPublic()
    {
        var source = """
            public sealed struct Frozen { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        _ = driver.Compile(MakeOptions(filePath));

        Assert.NotNull(capture.CapturedModel);

        var symbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "Frozen" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(symbol);

        var structType = Assert.IsType<StructType>(symbol!.DeclaredType);
        Assert.True(structType.IsSealed);
        Assert.Equal(Accessibility.Public, structType.Accessibility);
    }

    [Fact]
    public void NonSealedPublicStruct_HasIsSealedFalse_AndAccessibilityPublic()
    {
        var source = """
            public struct Flexible { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        _ = driver.Compile(MakeOptions(filePath));

        Assert.NotNull(capture.CapturedModel);

        var symbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "Flexible" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(symbol);

        var structType = Assert.IsType<StructType>(symbol!.DeclaredType);
        Assert.False(structType.IsSealed);
        Assert.Equal(Accessibility.Public, structType.Accessibility);
    }

    [Fact]
    public void SealedInternalStruct_HasIsSealedTrue_AndAccessibilityInternal()
    {
        var source = """
            internal sealed struct FrozenInternal { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        _ = driver.Compile(MakeOptions(filePath));

        Assert.NotNull(capture.CapturedModel);

        var symbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "FrozenInternal" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(symbol);

        var structType = Assert.IsType<StructType>(symbol!.DeclaredType);
        Assert.True(structType.IsSealed);
        Assert.Equal(Accessibility.Internal, structType.Accessibility);
    }

    [Fact]
    public void NonSealedInternalStruct_HasIsSealedFalse_AndAccessibilityInternal()
    {
        var source = """
            internal struct FlexibleInternal { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        _ = driver.Compile(MakeOptions(filePath));

        Assert.NotNull(capture.CapturedModel);

        var symbol = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "FlexibleInternal" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(symbol);

        var structType = Assert.IsType<StructType>(symbol!.DeclaredType);
        Assert.False(structType.IsSealed);
        Assert.Equal(Accessibility.Internal, structType.Accessibility);
    }

    // ─── Independence: same accessibility, different sealed ──────────────

    [Fact]
    public void SameAccessibility_DifferentSealedFlag_AreIndependent()
    {
        // Both classes are public, but one is sealed and one is not.
        // This confirms IsSealed is independent of the Accessibility value.
        var source = """
            public sealed class SealedWidget { }
            public class OpenWidget { }
            public sealed struct SealedGadget { }
            public struct OpenGadget { }
            """;
        var filePath = Path.Combine(scratchDir, "Program.mist");
        File.WriteAllText(filePath, source);

        var capture = new CapturingSemanticModelBuilder();
        var driver = CreateDriverWithCapture(capture);
        _ = driver.Compile(MakeOptions(filePath));

        Assert.NotNull(capture.CapturedModel);

        // Sealed class
        var sealedClassSym = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "SealedWidget" && s.Kind == SymbolKind.Class);
        Assert.NotNull(sealedClassSym);
        var sealedClass = Assert.IsType<ClassType>(sealedClassSym!.DeclaredType);
        Assert.True(sealedClass.IsSealed);
        Assert.Equal(Accessibility.Public, sealedClass.Accessibility);

        // Non-sealed class
        var openClassSym = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "OpenWidget" && s.Kind == SymbolKind.Class);
        Assert.NotNull(openClassSym);
        var openClass = Assert.IsType<ClassType>(openClassSym!.DeclaredType);
        Assert.False(openClass.IsSealed);
        Assert.Equal(Accessibility.Public, openClass.Accessibility);

        // Sealed struct
        var sealedStructSym = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "SealedGadget" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(sealedStructSym);
        var sealedStruct = Assert.IsType<StructType>(sealedStructSym!.DeclaredType);
        Assert.True(sealedStruct.IsSealed);
        Assert.Equal(Accessibility.Public, sealedStruct.Accessibility);

        // Non-sealed struct
        var openStructSym = capture.CapturedModel!.AllSymbols
            .FirstOrDefault(s => s.Name == "OpenGadget" && s.Kind == SymbolKind.Struct);
        Assert.NotNull(openStructSym);
        var openStruct = Assert.IsType<StructType>(openStructSym!.DeclaredType);
        Assert.False(openStruct.IsSealed);
        Assert.Equal(Accessibility.Public, openStruct.Accessibility);
    }
}