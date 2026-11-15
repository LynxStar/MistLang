using System.Reflection;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.ProgramModel.Model.Enums;
using Mist.Tests.InteropFixtures;
using Property = FsCheck.Property;

namespace Mist.Tests.Analysis;

public class ClrAccessibilityProjectionPropertyTests
{
    private static readonly Type FixtureType = typeof(AllAccessibilityLevels);

    private static readonly FieldInfo[] AllFields = FixtureType
        .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo[] AllMethods = FixtureType
        .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
        .Where(m => !m.IsSpecialName)
        .ToArray();

    private static readonly PropertyInfo[] AllProperties = FixtureType
        .GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

    private static readonly Type[] AllNestedTypes = FixtureType.GetNestedTypes(
        BindingFlags.Public | BindingFlags.NonPublic);

    private static Accessibility ExpectedForField(FieldInfo field)
    {
        if(field.IsPublic)
        {
            return Accessibility.Public;
        }

        if(field.IsFamily || field.IsFamilyOrAssembly)
        {
            return Accessibility.Protected;
        }

        return Accessibility.Private;
    }

    private static Accessibility ExpectedForMethod(MethodBase method)
    {
        if(method.IsPublic)
        {
            return Accessibility.Public;
        }

        if(method.IsFamily || method.IsFamilyOrAssembly)
        {
            return Accessibility.Protected;
        }

        return Accessibility.Private;
    }

    private static Accessibility ExpectedForNestedType(Type nestedType)
    {
        if(nestedType.IsNestedPublic)
        {
            return Accessibility.Public;
        }

        if(nestedType.IsNestedFamily || nestedType.IsNestedFamORAssem)
        {
            return Accessibility.Protected;
        }

        return Accessibility.Private;
    }

    private static Accessibility ExpectedForProperty(PropertyInfo property)
    {
        var getMethod = property.GetMethod;
        var setMethod = property.SetMethod;

        if(getMethod is { IsPublic: true } || setMethod is { IsPublic: true })
        {
            return Accessibility.Public;
        }

        if(IsProtectedMethod(getMethod) || IsProtectedMethod(setMethod))
        {
            return Accessibility.Protected;
        }

        return Accessibility.Private;
    }

    private static bool IsProtectedMethod(MethodInfo? method)
    {
        if(method is null)
        {
            return false;
        }

        return method.IsFamily || method.IsFamilyOrAssembly;
    }

#pragma warning disable CA1034

    public sealed record ClrAccessibilityScenario(
        string MemberKind,
        string MemberName,
        Accessibility Expected,
        Func<Accessibility> Project)
    {
        public override string ToString()
        {
            return $"{MemberKind}:{MemberName} -> {Expected}";
        }
    }

#pragma warning restore CA1034

    private static Gen<ClrAccessibilityScenario> GenFieldScenario()
    {
        return Gen.Elements(AllFields).Select(f => new ClrAccessibilityScenario(
            "Field", f.Name, ExpectedForField(f), () => ClrAccessibilityMap.FromField(f)));
    }

    private static Gen<ClrAccessibilityScenario> GenMethodScenario()
    {
        return Gen.Elements(AllMethods).Select(m => new ClrAccessibilityScenario(
            "Method", m.Name, ExpectedForMethod(m), () => ClrAccessibilityMap.FromMethod(m)));
    }

    private static Gen<ClrAccessibilityScenario> GenPropertyScenario()
    {
        return Gen.Elements(AllProperties).Select(p => new ClrAccessibilityScenario(
            "Property", p.Name, ExpectedForProperty(p), () => ClrAccessibilityMap.FromProperty(p)));
    }

    private static Gen<ClrAccessibilityScenario> GenNestedTypeScenario()
    {
        return Gen.Elements(AllNestedTypes).Select(t => new ClrAccessibilityScenario(
            "NestedType", t.Name, ExpectedForNestedType(t), () => ClrAccessibilityMap.FromNestedType(t)));
    }

    private static Gen<ClrAccessibilityScenario> GenAnyScenario()
    {
        return Gen.OneOf(
            GenFieldScenario(),
            GenMethodScenario(),
            GenPropertyScenario(),
            GenNestedTypeScenario());
    }

    public static Arbitrary<ClrAccessibilityScenario> ArbitraryClrAccessibilityScenario()
    {
        return GenAnyScenario().ToArbitrary();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ClrAccessibilityProjectionPropertyTests) })]
    public Property ClrAccessibility_ProjectsToCorrectMistLevel(ClrAccessibilityScenario scenario)
    {
        var actual = scenario.Project();

        var matchesExpected = actual == scenario.Expected;
        var neverInternal = actual != Accessibility.Internal;

        return (matchesExpected && neverInternal).ToProperty()
            .Label($"Expected {scenario.Expected}, got {actual} for {scenario}. " +
                   $"Internal produced: {!neverInternal}");
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ClrAccessibilityProjectionPropertyTests) })]
    public Property ClrAccessibility_NeverProducesInternal(ClrAccessibilityScenario scenario)
    {
        var actual = scenario.Project();

        return (actual != Accessibility.Internal).ToProperty()
            .Label($"Internal was produced for {scenario}");
    }

    [Fact]
    public void FromField_MapsAllSixAccessibilityForms()
    {
        var fieldMap = AllFields.ToDictionary(f => f.Name, ClrAccessibilityMap.FromField);

        Assert.Equal(Accessibility.Public, fieldMap["PublicField"]);
        Assert.Equal(Accessibility.Private, fieldMap["privateField"]);
        Assert.Equal(Accessibility.Protected, fieldMap["ProtectedField"]);
        Assert.Equal(Accessibility.Private, fieldMap["internalField"]);
        Assert.Equal(Accessibility.Protected, fieldMap["ProtectedInternalField"]);
        Assert.Equal(Accessibility.Private, fieldMap["privateProtectedField"]);
    }

    [Fact]
    public void FromMethod_MapsAllSixAccessibilityForms()
    {
        var methodMap = AllMethods.ToDictionary(m => m.Name, ClrAccessibilityMap.FromMethod);

        Assert.Equal(Accessibility.Public, methodMap["PublicMethod"]);
        Assert.Equal(Accessibility.Private, methodMap["PrivateMethod"]);
        Assert.Equal(Accessibility.Protected, methodMap["ProtectedMethod"]);
        Assert.Equal(Accessibility.Private, methodMap["InternalMethod"]);
        Assert.Equal(Accessibility.Protected, methodMap["ProtectedInternalMethod"]);
        Assert.Equal(Accessibility.Private, methodMap["PrivateProtectedMethod"]);
    }

    [Fact]
    public void FromProperty_MapsAllSixAccessibilityForms()
    {
        var propMap = AllProperties.ToDictionary(p => p.Name, ClrAccessibilityMap.FromProperty);

        Assert.Equal(Accessibility.Public, propMap["PublicProp"]);
        Assert.Equal(Accessibility.Private, propMap["PrivateProp"]);
        Assert.Equal(Accessibility.Protected, propMap["ProtectedProp"]);
        Assert.Equal(Accessibility.Private, propMap["InternalProp"]);
        Assert.Equal(Accessibility.Protected, propMap["ProtectedInternalProp"]);
        Assert.Equal(Accessibility.Private, propMap["PrivateProtectedProp"]);
    }

    [Fact]
    public void FromNestedType_MapsAllSixAccessibilityForms()
    {
        var nestedMap = AllNestedTypes.ToDictionary(t => t.Name, ClrAccessibilityMap.FromNestedType);

        Assert.Equal(Accessibility.Public, nestedMap["NestedPublic"]);
        Assert.Equal(Accessibility.Private, nestedMap["NestedPrivate"]);
        Assert.Equal(Accessibility.Protected, nestedMap["NestedProtected"]);
        Assert.Equal(Accessibility.Private, nestedMap["NestedInternal"]);
        Assert.Equal(Accessibility.Protected, nestedMap["NestedProtectedInternal"]);
        Assert.Equal(Accessibility.Private, nestedMap["NestedPrivateProtected"]);
    }

    [Fact]
    public void AllMappings_NeverProduceInternal()
    {
        var allResults = new List<Accessibility>();
        allResults.AddRange(AllFields.Select(ClrAccessibilityMap.FromField));
        allResults.AddRange(AllMethods.Select(ClrAccessibilityMap.FromMethod));
        allResults.AddRange(AllProperties.Select(ClrAccessibilityMap.FromProperty));
        allResults.AddRange(AllNestedTypes.Select(ClrAccessibilityMap.FromNestedType));

        Assert.DoesNotContain(Accessibility.Internal, allResults);
    }
}