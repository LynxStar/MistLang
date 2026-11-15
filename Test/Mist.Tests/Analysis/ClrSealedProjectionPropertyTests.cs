using System.Reflection;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Model.Types;
using Property = FsCheck.Property;

namespace Mist.Tests.Analysis;

public class ClrSealedProjectionPropertyTests
{
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

    private static readonly Type[] SealedClasses =
    [
        typeof(string),
        typeof(Version),
        typeof(StackOverflowException),
        typeof(IndexOutOfRangeException),
        typeof(AttributeUsageAttribute),
    ];

    private static readonly Type[] NonSealedClasses =
    [
        typeof(object),
        typeof(Exception),
        typeof(List<int>),
        typeof(Dictionary<string, int>),
        typeof(EventArgs),
        typeof(Uri),
    ];

    private static readonly Type[] StaticClasses =
    [
        typeof(Math),
        typeof(Console),
        typeof(GC),
        typeof(Environment),
        typeof(Convert),
    ];

    private static readonly Type[] AbstractClasses =
    [
        typeof(Stream),
        typeof(Array),
        typeof(Encoding),
        typeof(MemberInfo),
        typeof(Type),
    ];

    private static readonly Type[] Structs =
    [
        typeof(int),
        typeof(double),
        typeof(DateTime),
        typeof(Guid),
        typeof(TimeSpan),
        typeof(KeyValuePair<string, int>),
    ];

    private static bool ExpectedSealedFlag(Type clrType)
    {
        var isStaticClass = clrType.IsAbstract && clrType.IsSealed;
        return clrType.IsSealed && !isStaticClass;
    }

    private static bool? ProjectSealedFlag(Type clrType)
    {
        var shell = TypeProjector.CreateShell(clrType, clrType.Namespace ?? "");
        if(shell is ConcreteType concreteType)
        {
            return concreteType.IsSealed;
        }

        return null;
    }

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

    public static Arbitrary<ClrSealedScenario> ArbitraryClrSealedScenario()
    {
        return GenAnyScenario().ToArbitrary();
    }

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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ClrSealedProjectionPropertyTests) })]
    public Property StaticClass_NeverProjectsAsSealed(ClrSealedScenario scenario)
    {
        if(!scenario.ClrType.IsAbstract || !scenario.ClrType.IsSealed)
        {
            return true.ToProperty().Label("Not a static class -- skipped");
        }

        var actual = ProjectSealedFlag(scenario.ClrType);

        return (actual == false).ToProperty()
            .Label($"Static class {scenario.ClrType.Name} should NOT project as sealed, got {actual}");
    }

    [Fact]
    public void SealedClass_ProjectsAsSealed()
    {
        foreach(var type in SealedClasses)
        {
            var actual = ProjectSealedFlag(type);
            Assert.True(actual == true, $"Sealed class {type.Name} should project as sealed, got {actual}");
        }
    }

    [Fact]
    public void NonSealedClass_ProjectsAsNotSealed()
    {
        foreach(var type in NonSealedClasses)
        {
            var actual = ProjectSealedFlag(type);
            Assert.True(actual == false, $"Non-sealed class {type.Name} should project as not sealed, got {actual}");
        }
    }

    [Fact]
    public void StaticClass_ProjectsAsNotSealed()
    {
        foreach(var type in StaticClasses)
        {
            Assert.True(type.IsAbstract && type.IsSealed,
                $"{type.Name} should be abstract+sealed (static class) at CLR level");

            var actual = ProjectSealedFlag(type);
            Assert.True(actual == false,
                $"Static class {type.Name} should project as NOT sealed (exception), got {actual}");
        }
    }

    [Fact]
    public void AbstractClass_ProjectsAsNotSealed()
    {
        foreach(var type in AbstractClasses)
        {
            var actual = ProjectSealedFlag(type);
            Assert.True(actual == false, $"Abstract class {type.Name} should project as not sealed, got {actual}");
        }
    }

    [Fact]
    public void Struct_ProjectsAsSealed()
    {
        foreach(var type in Structs)
        {
            Assert.True(type.IsSealed, $"{type.Name} should be sealed at CLR level");
            Assert.False(type.IsAbstract, $"{type.Name} should not be abstract at CLR level");

            var actual = ProjectSealedFlag(type);
            Assert.True(actual == true, $"Struct {type.Name} should project as sealed, got {actual}");
        }
    }

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