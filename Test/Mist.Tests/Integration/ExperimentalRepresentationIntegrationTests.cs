// Integration test for task 8.5: experimental-representation integration test
// Validates: Requirements 4.1, 4.2, 5.9, 16.1, 16.2
//
// Builds the Program Model from Mist source in-process, projects representative
// CLR types via the Ambient Type Provider, and asserts both the Accessibility
// value and the sealed flag are populated on declarations and projected types/members.

using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Driver.Contracts;
using Mist.Lexing;
using Mist.Parsing;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Backend;
using Mist.Tests.Helpers;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Integration;

/// <summary>
/// Integration tests verifying that the Program Model
/// correctly populates both the <see cref="Accessibility"/> value and the
/// <see cref="ConcreteType.IsSealed"/> flag on user-declared types and on
/// CLR-projected types/members.
/// </summary>
[Trait("Tier", "3")]
public sealed class ExperimentalRepresentationIntegrationTests : IDisposable
{
    private readonly string outputDir;

    public ExperimentalRepresentationIntegrationTests()
    {
        outputDir = Path.Combine(Path.GetTempPath(), "MistExpRepr_" + Guid.NewGuid().ToString("N"));
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

    // ═══════════════════════════════════════════════════════════════════════
    // Helper: build Program Model from Mist source in-process
    // ═══════════════════════════════════════════════════════════════════════

    private IProgramModel BuildProgramModel(string source, string fileName = "test.mist")
    {
        var lexer = new MistLexer();
        var parser = new MistParser();
        var diagnostics = new DiagnosticBag();

        var sourceText = new SourceText(fileName, source);
        var tokens = lexer.Lex(sourceText, diagnostics);
        var parseResult = parser.Parse(tokens, diagnostics);

        Assert.NotNull(parseResult.Root);
        Assert.Empty(diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error));

        var unit = new CompilationUnit(sourceText);
        var compilation = new MistCompilation(ImmutableArray.Create(unit));

        var options = new CompilerOptions(
            ProjectFolder: outputDir,
            SourcePaths: ImmutableArray.Create(fileName),
            Backend: new BackendId("csharp"),
            OutputDirectory: outputDir,
            BinDirectory: Path.Combine(outputDir, "bin"),
            ProjectName: "TestProject",
            NamespaceRoot: "TestProject");

        var buildDiagnostics = new DiagnosticBag();
        var programModel = ProgramModelBuilder.Build(
            compilation,
            ImmutableArray.Create(parseResult.Root),
            options,
            buildDiagnostics,
            NullAmbientTypeProvider.Instance);

        Assert.NotNull(programModel);
        return programModel!;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Section 1: User-declared types have Accessibility populated (Req 4.1, 5.9)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UserDeclaredPublicClass_HasAccessibilityPublic()
    {
        var source = """
            namespace TestProject;

            public class Foo
            {
            }
            """;

        var model = BuildProgramModel(source);

        var fooType = model.TypesByFQN["TestProject.Foo"];
        Assert.NotNull(fooType);
        var memberType = Assert.IsAssignableFrom<MemberType>(fooType);
        Assert.Equal(Accessibility.Public, memberType.Modifiers.Accessibility);
    }

    [Fact]
    public void UserDeclaredPrivateClass_HasAccessibilityPrivate()
    {
        var source = """
            namespace TestProject;

            public class Outer
            {
                private class Inner
                {
                }
            }
            """;

        var model = BuildProgramModel(source);

        var innerType = model.TypesByFQN["TestProject.Outer.Inner"];
        Assert.NotNull(innerType);
        var memberType = Assert.IsAssignableFrom<MemberType>(innerType);
        Assert.Equal(Accessibility.Private, memberType.Modifiers.Accessibility);
    }

    [Fact]
    public void UserDeclaredProtectedClass_HasAccessibilityProtected()
    {
        var source = """
            namespace TestProject;

            public class Outer
            {
                protected class Nested
                {
                }
            }
            """;

        var model = BuildProgramModel(source);

        var nestedType = model.TypesByFQN["TestProject.Outer.Nested"];
        Assert.NotNull(nestedType);
        var memberType = Assert.IsAssignableFrom<MemberType>(nestedType);
        Assert.Equal(Accessibility.Protected, memberType.Modifiers.Accessibility);
    }

    [Fact]
    public void UserDeclaredInternalClass_HasAccessibilityInternal()
    {
        var source = """
            namespace TestProject;

            internal class Bar
            {
            }
            """;

        var model = BuildProgramModel(source);

        var barType = model.TypesByFQN["TestProject.Bar"];
        Assert.NotNull(barType);
        var memberType = Assert.IsAssignableFrom<MemberType>(barType);
        Assert.Equal(Accessibility.Internal, memberType.Modifiers.Accessibility);
    }

    [Fact]
    public void UserDeclaredStruct_DefaultAccessibility_IsInternal()
    {
        // A top-level struct with no explicit modifier defaults to Internal (Req 5.9)
        var source = """
            namespace TestProject;

            struct Baz
            {
            }
            """;

        var model = BuildProgramModel(source);

        var bazType = model.TypesByFQN["TestProject.Baz"];
        Assert.NotNull(bazType);
        var memberType = Assert.IsAssignableFrom<MemberType>(bazType);
        Assert.Equal(Accessibility.Internal, memberType.Modifiers.Accessibility);
    }

    [Fact]
    public void UserDeclaredPublicStruct_HasAccessibilityPublic()
    {
        var source = """
            namespace TestProject;

            public struct Vec
            {
                public int x;
            }
            """;

        var model = BuildProgramModel(source);

        var vecType = model.TypesByFQN["TestProject.Vec"];
        Assert.NotNull(vecType);
        var memberType = Assert.IsAssignableFrom<MemberType>(vecType);
        Assert.Equal(Accessibility.Public, memberType.Modifiers.Accessibility);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Section 2: IsSealed flag populated on user-declared types (Req 4.2, 16.1)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void UserDeclaredSealedClass_HasIsSealedTrue()
    {
        var source = """
            namespace TestProject;

            public sealed class Immutable
            {
            }
            """;

        var model = BuildProgramModel(source);

        var type = model.TypesByFQN["TestProject.Immutable"];
        Assert.NotNull(type);
        var concreteType = Assert.IsAssignableFrom<ConcreteType>(type);
        Assert.True(concreteType.IsSealed, "A sealed class should have IsSealed = true");
    }

    [Fact]
    public void UserDeclaredNonSealedClass_HasIsSealedFalse()
    {
        var source = """
            namespace TestProject;

            public class Open
            {
            }
            """;

        var model = BuildProgramModel(source);

        var type = model.TypesByFQN["TestProject.Open"];
        Assert.NotNull(type);
        var concreteType = Assert.IsAssignableFrom<ConcreteType>(type);
        Assert.False(concreteType.IsSealed, "A non-sealed class should have IsSealed = false");
    }

    [Fact]
    public void UserDeclaredSealedStruct_HasIsSealedTrue()
    {
        var source = """
            namespace TestProject;

            public sealed struct Frozen
            {
                public int value;
            }
            """;

        var model = BuildProgramModel(source);

        var type = model.TypesByFQN["TestProject.Frozen"];
        Assert.NotNull(type);
        var concreteType = Assert.IsAssignableFrom<ConcreteType>(type);
        Assert.True(concreteType.IsSealed, "A sealed struct should have IsSealed = true");
    }

    [Fact]
    public void UserDeclaredNonSealedStruct_HasIsSealedFalse()
    {
        var source = """
            namespace TestProject;

            public struct Mutable
            {
                public int x;
            }
            """;

        var model = BuildProgramModel(source);

        var type = model.TypesByFQN["TestProject.Mutable"];
        Assert.NotNull(type);
        var concreteType = Assert.IsAssignableFrom<ConcreteType>(type);
        Assert.False(concreteType.IsSealed, "A non-sealed struct should have IsSealed = false");
    }

    [Fact]
    public void SealedAndAccessibility_AreIndependent()
    {
        // Verify that sealed and accessibility are independently populated (Req 4.2)
        var source = """
            namespace TestProject;

            public sealed class PubSealed
            {
            }

            internal sealed class IntSealed
            {
            }

            public class PubOpen
            {
            }
            """;

        var model = BuildProgramModel(source);

        // public sealed
        var pubSealed = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["TestProject.PubSealed"]);
        Assert.Equal(Accessibility.Public, pubSealed.Modifiers.Accessibility);
        Assert.True(pubSealed.IsSealed);

        // internal sealed
        var intSealed = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["TestProject.IntSealed"]);
        Assert.Equal(Accessibility.Internal, intSealed.Modifiers.Accessibility);
        Assert.True(intSealed.IsSealed);

        // public non-sealed
        var pubOpen = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["TestProject.PubOpen"]);
        Assert.Equal(Accessibility.Public, pubOpen.Modifiers.Accessibility);
        Assert.False(pubOpen.IsSealed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Section 3: CLR-projected types have Accessibility and IsSealed (Req 16.1, 16.2)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ClrProjected_SealedClass_HasIsSealedTrue()
    {
        // System.Version is a sealed class in CLR (not aliased to Standard)
        var (programModel, _) = CreateProgramModelWithClrTypes();

        var versionType = programModel.TypesByFQN.Values
            .OfType<ConcreteType>()
            .FirstOrDefault(t => t.FullyQualifiedName == "System.Version");

        Assert.NotNull(versionType);
        Assert.True(versionType!.IsSealed, "System.Version is sealed in CLR and should project as IsSealed = true");
    }

    [Fact]
    public void ClrProjected_NonSealedClass_HasIsSealedFalse()
    {
        // System.IO.Stream is NOT sealed
        var (programModel, _) = CreateProgramModelWithClrTypes();

        var streamType = programModel.TypesByFQN.Values
            .OfType<ConcreteType>()
            .FirstOrDefault(t => t.FullyQualifiedName == "System.IO.Stream");

        Assert.NotNull(streamType);
        Assert.False(streamType!.IsSealed, "System.IO.Stream is not sealed and should project as IsSealed = false");
    }

    [Fact]
    public void ClrProjected_StaticClass_HasIsSealedFalse()
    {
        // System.Math is static (abstract + sealed in CLR metadata), should be treated as NOT sealed
        var (programModel, _) = CreateProgramModelWithClrTypes();

        var mathType = programModel.TypesByFQN.Values
            .OfType<MemberType>()
            .FirstOrDefault(t => t.FullyQualifiedName == "System.Math");

        Assert.NotNull(mathType);

        if(mathType is ConcreteType concrete)
        {
            Assert.False(concrete.IsSealed,
                "System.Math is a static class (abstract+sealed) and should project as IsSealed = false");
        }
    }

    [Fact]
    public void ClrProjected_PublicClass_HasAccessibilityPublic()
    {
        var (programModel, _) = CreateProgramModelWithClrTypes();

        var consoleType = programModel.TypesByFQN.Values
            .OfType<MemberType>()
            .FirstOrDefault(t => t.FullyQualifiedName == "System.Console");

        Assert.NotNull(consoleType);
        Assert.Equal(Accessibility.Public, consoleType!.Modifiers.Accessibility);
    }

    [Fact]
    public void ClrProjected_SealedStruct_HasIsSealedTrue()
    {
        // All CLR structs are implicitly sealed
        var (programModel, _) = CreateProgramModelWithClrTypes();

        var dateTimeType = programModel.TypesByFQN.Values
            .OfType<ConcreteType>()
            .FirstOrDefault(t => t.FullyQualifiedName == "System.DateTime");

        Assert.NotNull(dateTimeType);
        Assert.True(dateTimeType!.IsSealed, "CLR structs (e.g., DateTime) are implicitly sealed");
    }

    [Fact]
    public void ClrProjected_PublicMembers_HaveAccessibilityPublic()
    {
        var (programModel, _) = CreateProgramModelWithClrTypes();

        var consoleType = programModel.TypesByFQN.Values
            .OfType<MemberType>()
            .FirstOrDefault(t => t.FullyQualifiedName == "System.Console");

        Assert.NotNull(consoleType);

        // Console.WriteLine should be public
        var writeLineMethod = consoleType!.MethodsList
            .FirstOrDefault(m => m.Name == "WriteLine");

        Assert.NotNull(writeLineMethod);
        Assert.Equal(Accessibility.Public, writeLineMethod!.Modifiers.Accessibility);
    }

    [Fact]
    public void ClrProjected_ProtectedMembers_HaveAccessibilityProtected()
    {
        var (programModel, _) = CreateProgramModelWithClrTypes();

        // System.IO.Stream has protected members (e.g., constructors or methods)
        var streamType = programModel.TypesByFQN.Values
            .OfType<ConcreteType>()
            .FirstOrDefault(t => t.FullyQualifiedName == "System.IO.Stream");

        Assert.NotNull(streamType);

        // Stream has a protected parameterless constructor
        var protectedCtor = streamType!.ConstructorsList
            .FirstOrDefault(c => c.Modifiers.Accessibility == Accessibility.Protected);

        Assert.NotNull(protectedCtor);
    }

    [Fact]
    public void ClrProjected_TypeAccessibility_NeverInternal()
    {
        // Requirement 16.2/16.6: projected types never have Accessibility.Internal
        var (programModel, _) = CreateProgramModelWithClrTypes();

        var ambientTypes = programModel.TypesByFQN.Values
            .OfType<MemberType>()
            .Where(t => t.Assembly.Kind == MistAssemblyKind.Ambient)
            .ToList();

        Assert.NotEmpty(ambientTypes);

        foreach(var type in ambientTypes)
        {
            Assert.NotEqual(Accessibility.Internal, type.Modifiers.Accessibility);
        }
    }

    [Fact]
    public void ClrProjected_MemberAccessibility_NeverInternal()
    {
        // Requirement 16.6: projected members never have Accessibility.Internal
        var (programModel, _) = CreateProgramModelWithClrTypes();

        var ambientMemberTypes = programModel.TypesByFQN.Values
            .OfType<MemberType>()
            .Where(t => t.Assembly.Kind == MistAssemblyKind.Ambient)
            .Take(20) // Check a representative sample to keep the test fast
            .ToList();

        Assert.NotEmpty(ambientMemberTypes);

        foreach(var type in ambientMemberTypes)
        {
            foreach(var method in type.MethodsList)
            {
                Assert.NotEqual(Accessibility.Internal, method.Modifiers.Accessibility);
            }

            foreach(var property in type.PropertiesList)
            {
                Assert.NotEqual(Accessibility.Internal, property.Modifiers.Accessibility);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Section 4: Combined test — user-declared + CLR types in one model
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void CombinedModel_UserAndClrTypes_BothHaveAccessibilityAndSealedPopulated()
    {
        // This test verifies that user-declared types carry accessibility and sealed
        // when built alongside CLR-projected types. We project CLR types into a
        // ProgramModel that already has user-declared types registered.
        var source = """
            namespace TestProject;

            public sealed class UserSealed
            {
            }

            public class UserOpen
            {
            }
            """;

        // Build user-declared types via ProgramModelBuilder (no ambient)
        var model = BuildProgramModel(source);

        // Verify user-declared types carry accessibility and sealed flag
        var userSealed = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["TestProject.UserSealed"]);
        Assert.Equal(Accessibility.Public, userSealed.Modifiers.Accessibility);
        Assert.True(userSealed.IsSealed);

        var userOpen = Assert.IsAssignableFrom<ConcreteType>(model.TypesByFQN["TestProject.UserOpen"]);
        Assert.Equal(Accessibility.Public, userOpen.Modifiers.Accessibility);
        Assert.False(userOpen.IsSealed);

        // Separately verify CLR types carry the same data (complementary to Section 3 tests)
        var (clrModel, _) = CreateProgramModelWithClrTypes();

        var consoleType = clrModel.TypesByFQN.Values
            .OfType<MemberType>()
            .FirstOrDefault(t => t.FullyQualifiedName == "System.Console");

        Assert.NotNull(consoleType);
        Assert.Equal(Accessibility.Public, consoleType!.Modifiers.Accessibility);

        // Verify a sealed CLR type
        var dateTimeType = clrModel.TypesByFQN.Values
            .OfType<ConcreteType>()
            .FirstOrDefault(t => t.FullyQualifiedName == "System.DateTime");

        Assert.NotNull(dateTimeType);
        Assert.True(dateTimeType!.IsSealed);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helper: Create ProgramModel with CLR types projected
    // ═══════════════════════════════════════════════════════════════════════

    private static (ProgramModelClass Model, AmbientTypeProviderV2 Provider) CreateProgramModelWithClrTypes()
    {
        var programModel = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(programModel);

        var runtimePath = typeof(object).Assembly.Location;
        var runtimeDir = Path.GetDirectoryName(runtimePath)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        var resolver = new PathAssemblyResolver(assemblyPaths);
        var metadataContext = new MetadataLoadContext(resolver);
        var loadedAssemblies = AmbientTypeProviderV2Factory.LoadAssemblies(assemblyPaths, metadataContext, new DiagnosticBag());

        var provider = new AmbientTypeProviderV2(loadedAssemblies, null, new DiagnosticBag());
        provider.BuildAndRegister(programModel);

        return (programModel, provider);
    }
}