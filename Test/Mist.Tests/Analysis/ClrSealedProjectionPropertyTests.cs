// Feature: full-accessibility-model, Property 21: CLR sealed state projects to the first-class sealed flag with the static-class exception
// **Validates: Requirements 16.5**

using System.Reflection;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Model.Types;
using Property = FsCheck.Property;

namespace Mist.Tests.Analysis;

/// <summary>
/// Property-based tests for CLR sealed projection via <see cref="TypeProjector.CreateShell"/>.
///
/// Property 21: For any CLR type, the projected first-class sealed flag equals the type's
/// <c>IsSealed</c> state, except that a CLR static class (a type that is both abstract and sealed)
/// projects as neither sealed nor abstract.
///
/// The test exercises <see cref="TypeProjector.CreateShell"/> over real BCL types from reflection,
/// covering sealed classes, non-sealed classes, static classes (abstract+sealed), abstract classes,
/// and structs (which are sealed at the CLR level).
///
/// **Validates: Requirements 16.5 (Property 21)**
/// </summary>
public class ClrSealedProjectionPropertyTests
{
    // ─── Test data: real CLR types with known sealed/abstract shapes ─────

    /// <summary>
    /// A scenario pairing a CLR type with the expected <c>IsSealed</c> value after projection.
    /// </summary>
#pragma warning disable CA1034
    public sealed record ClrSealedScenario(
        Type ClrType,
        bool ExpectedIsSealed,
        string Description)
    {
        public override string ToString()
        {
            return $"{Description} ({ClrType.Name}): IsSealed={ClrType.IsSealed}, " +
                   $"IsAbstract={ClrType.IsAbstract} -> Expected={ExpectedIsSealed}";
        }
    }
#pragma warning restore CA1034

    // ─── Known CLR types organized by sealed shape ───────────────────────

    /// <summary>Sealed classes: CLR IsSealed=true, IsAbstract=false -> projects as sealed.</summary>
    private static readonly Type[] SealedClasses =
    [
        typeof(string),
        typeof(Version),
        typeof(StackOverflowException),
        typeof(IndexOutOfRangeException),
        typeof(AttributeUsageAttribute),
    ];

    /// <summary>Non-sealed, non-abstract classes: CLR IsSealed=false, IsAbstract=false -> not sealed.</summary>
    private static readonly Type[] NonSealedClasses =
    [
        typeof(object),
        typeof(Exception),
        typeof(List<int>),
        typeof(Dictionary<string, int>),
        typeof(EventArgs),
        typeof(Uri),
    ];

    /// <summary>
    /// Static classes (abstract+sealed at CLR level): CLR IsSealed=true AND IsAbstract=true
    /// -> projects as NOT sealed (the static-class exception).
    /// </summary>
    private static readonly Type[] StaticClasses =
    [
        typeof(Math),
        typeof(Console),
        typeof(GC),
        typeof(Environment),
        typeof(Convert),
    ];

    /// <summary>Abstract classes: CLR IsSealed=false, IsAbstract=true -> not sealed.</summary>
    private static readonly Type[] AbstractClasses =
    [
        typeof(Stream),
        typeof(Array),
        typeof(Encoding),
        typeof(MemberInfo),
        typeof(Type),
    ];

    /// <summary>
    /// Structs (value types): CLR IsSealed=true, IsAbstract=false -> projects as sealed.
    /// At the CLR level, all value types are sealed.
    /// </summary>
    private static readonly Type[] Structs =
    [
        typeof(int),
        typeof(double),
        typeof(DateTime),
        typeof(Guid),
        typeof(TimeSpan),
        typeof(KeyValuePair<string, int>),
    ];

    // ─── Expected mapping oracle ─────────────────────────────────────────

    /// <summary>
    /// Computes the expected <c>IsSealed</c> value for a projected CLR type.
    /// A static class (abstract+sealed) projects as NOT sealed; all other sealed types project as sealed.
    /// </summary>
    private static bool ExpectedSealedFlag(Type clrType)
    {
        var isStaticClass = clrType.IsAbstract && clrType.IsSealed;
        return clrType.IsSealed && !isStaticClass;
    }

    /// <summary>
    /// Projects a CLR type through <see cref="TypeProjector.CreateShell"/> and extracts the
    /// <c>IsSealed</c> flag from the resulting <see cref="ConcreteType"/>.
    /// Returns null if the projected type is not a <see cref="ConcreteType"/> (e.g., interfaces/enums).
    /// </summary>
    private static bool? ProjectSealedFlag(Type clrType)
    {
        var shell = TypeProjector.CreateShell(clrType, clrType.Namespace ?? "");
        if(shell is ConcreteType concreteType)
        {
            return concreteType.IsSealed;
        }

        return null;
    }

    // ─── Generators ──────────────────────────────────────────────────────

    private static Gen<ClrSealedScenario> GenSealedClassScenario()
    {
        return Gen.Elements(SealedClasses).Select(t =>
            new ClrSealedScenario(t, ExpectedSealedFlag(t), "SealedClass"));
    }

    private static Gen<ClrSealedScenario> GenNonSealedClassScenario()
    {
        return Gen.Elements(NonSealedClasses).Select(t =>
            new ClrSealedScenario(t, ExpectedSealedFlag(t), "NonSealedClass"));
    }

    private static Gen<ClrSealedScenario> GenStaticClassScenario()
    {
        return Gen.Elements(StaticClasses).Select(t =>
            new ClrSealedScenario(t, ExpectedSealedFlag(t), "StaticClass"));
    }

    private static Gen<ClrSealedScenario> GenAbstractClassScenario()
    {
        return Gen.Elements(AbstractClasses).Select(t =>
            new ClrSealedScenario(t, ExpectedSealedFlag(t), "AbstractClass"));
    }

    private static Gen<ClrSealedScenario> GenStructScenario()
    {
        return Gen.Elements(Structs).Select(t =>
            new ClrSealedScenario(t, ExpectedSealedFlag(t), "Struct"));
    }

    private static Gen<ClrSealedScenario> GenAnyScenario()
    {
        return Gen.OneOf(
            GenSealedClassScenario(),
            GenNonSealedClassScenario(),
            GenStaticClassScenario(),
            GenAbstractClassScenario(),
            GenStructScenario());
    }

    // ─── Arbitrary registration ──────────────────────────────────────────

    public static Arbitrary<ClrSealedScenario> ArbitraryClrSealedScenario()
    {
        return GenAnyScenario().ToArbitrary();
    }

    // ─── Property Tests ──────────────────────────────────────────────────

    /// <summary>
    /// Property 21: CLR sealed state projects to the first-class sealed flag with the
    /// static-class exception.
    ///
    /// For any CLR type, the projected first-class sealed flag equals <c>IsSealed</c>,
    /// except that a CLR static class (abstract+sealed) projects as neither sealed nor abstract.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ClrSealedProjectionPropertyTests) })]
    public Property ClrSealed_ProjectsToFirstClassFlag_WithStaticClassException(ClrSealedScenario scenario)
    {
        var actual = ProjectSealedFlag(scenario.ClrType);

        // All our test types are classes or structs, so they project to ConcreteType.
        var hasResult = actual.HasValue;
        var matchesExpected = actual == scenario.ExpectedIsSealed;

        return (hasResult && matchesExpected).ToProperty()
            .Label($"Expected IsSealed={scenario.ExpectedIsSealed}, got {actual} for {scenario}");
    }

    /// <summary>
    /// Property 21 (static-class invariant): A CLR static class (abstract+sealed) always
    /// projects as NOT sealed, because static classes should not prevent "inheritance"
    /// in the Mist model.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ClrSealedProjectionPropertyTests) })]
    public Property StaticClass_NeverProjectsAsSealed(ClrSealedScenario scenario)
    {
        if(!scenario.ClrType.IsAbstract || !scenario.ClrType.IsSealed)
        {
            // Not a static class, property is vacuously true for this scenario.
            return true.ToProperty().Label("Not a static class -- skipped");
        }

        var actual = ProjectSealedFlag(scenario.ClrType);

        return (actual == false).ToProperty()
            .Label($"Static class {scenario.ClrType.Name} should NOT project as sealed, got {actual}");
    }

    // ─── Exhaustive deterministic tests ──────────────────────────────────

    /// <summary>
    /// Sealed classes project as sealed (IsSealed == true).
    /// </summary>
    [Fact]
    public void SealedClass_ProjectsAsSealed()
    {
        foreach(var type in SealedClasses)
        {
            var actual = ProjectSealedFlag(type);
            Assert.True(actual == true, $"Sealed class {type.Name} should project as sealed, got {actual}");
        }
    }

    /// <summary>
    /// Non-sealed classes project as not sealed (IsSealed == false).
    /// </summary>
    [Fact]
    public void NonSealedClass_ProjectsAsNotSealed()
    {
        foreach(var type in NonSealedClasses)
        {
            var actual = ProjectSealedFlag(type);
            Assert.True(actual == false, $"Non-sealed class {type.Name} should project as not sealed, got {actual}");
        }
    }

    /// <summary>
    /// Static classes (abstract+sealed at CLR level) project as NOT sealed -- the exception.
    /// </summary>
    [Fact]
    public void StaticClass_ProjectsAsNotSealed()
    {
        foreach(var type in StaticClasses)
        {
            // Verify our test data: static classes are both abstract and sealed at CLR level.
            Assert.True(type.IsAbstract && type.IsSealed,
                $"{type.Name} should be abstract+sealed (static class) at CLR level");

            var actual = ProjectSealedFlag(type);
            Assert.True(actual == false,
                $"Static class {type.Name} should project as NOT sealed (exception), got {actual}");
        }
    }

    /// <summary>
    /// Abstract classes project as not sealed (IsSealed == false).
    /// </summary>
    [Fact]
    public void AbstractClass_ProjectsAsNotSealed()
    {
        foreach(var type in AbstractClasses)
        {
            var actual = ProjectSealedFlag(type);
            Assert.True(actual == false, $"Abstract class {type.Name} should project as not sealed, got {actual}");
        }
    }

    /// <summary>
    /// Structs (value types, sealed at CLR level) project as sealed (IsSealed == true).
    /// </summary>
    [Fact]
    public void Struct_ProjectsAsSealed()
    {
        foreach(var type in Structs)
        {
            // Verify our test data: structs are sealed at CLR level.
            Assert.True(type.IsSealed, $"{type.Name} should be sealed at CLR level");
            Assert.False(type.IsAbstract, $"{type.Name} should not be abstract at CLR level");

            var actual = ProjectSealedFlag(type);
            Assert.True(actual == true, $"Struct {type.Name} should project as sealed, got {actual}");
        }
    }

    /// <summary>
    /// Verifies the oracle: all scenarios have the expected relationship between CLR flags
    /// and the expected <c>IsSealed</c> value.
    /// </summary>
    [Fact]
    public void OracleConsistency_AllScenariosMatchLogic()
    {
        var allTypes = SealedClasses
            .Concat(NonSealedClasses)
            .Concat(StaticClasses)
            .Concat(AbstractClasses)
            .Concat(Structs);

        foreach(var type in allTypes)
        {
            var expected = ExpectedSealedFlag(type);
            var actual = ProjectSealedFlag(type);

            _ = Assert.NotNull(actual);
            Assert.Equal(expected, actual.Value);
        }
    }
}