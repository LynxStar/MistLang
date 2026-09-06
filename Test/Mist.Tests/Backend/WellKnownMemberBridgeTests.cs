using System.Collections.Immutable;
using System.Reflection;
using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Backend;

/// <summary>
/// Merging a bridged well-known type's CLR members into the Standard declaration that stands in for
/// it. The Mist-declared members are added here by hand, because this fixture registers shells and
/// never parses Bootstrap/String.mist -- which makes the collisions settable rather than incidental,
/// and is the only way to state what the merge must do about each one.
/// </summary>
public sealed class WellKnownMemberBridgeTests : IDisposable
{
    private readonly ProgramModelClass programModel;
    private readonly AmbientTypeProviderV2 provider;
    private readonly MetadataLoadContext metadataContext;
    private readonly ConcreteType stringType;

    public WellKnownMemberBridgeTests()
    {
        programModel = new ProgramModelClass();
        AmbientProviderTestHelper.RegisterStandardTypes(programModel);

        var runtimeDir = Path.GetDirectoryName(typeof(object).Assembly.Location)!;
        var assemblyPaths = Directory.GetFiles(runtimeDir, "*.dll").ToImmutableArray();

        metadataContext = new MetadataLoadContext(new PathAssemblyResolver(assemblyPaths));

        var loaded = AmbientTypeProviderV2Factory.LoadAssemblies(
            assemblyPaths, metadataContext, new DiagnosticBag());

        provider = new AmbientTypeProviderV2(loaded, null, new DiagnosticBag());

        stringType = (ConcreteType)programModel.TypesByFQN["Standard.String"];
        var int32 = programModel.TypesByFQN["Standard.Int32"];
        var intrinsic = new MemberModifiers { Accessibility = Accessibility.Public, IsIntrinsic = true };

        stringType.PropertiesList.Add(new Property("Length", int32, intrinsic) { Getter = new PropertyAccessor() });
        stringType.MethodsList.Add(new Method("Clone", stringType, [], intrinsic));
        stringType.MethodsList.Add(
            new Method("Substring", stringType, [new Parameter("startIndex", int32)], intrinsic));

        provider.BuildAndRegister(programModel);
        provider.BridgeWellKnownMembers(programModel);
    }

    public void Dispose()
    {
        provider.Dispose();
        metadataContext.Dispose();
    }

    private IEnumerable<Method> MethodsNamed(string name)
    {
        return stringType.MethodsList.Where(method => method.Name == name);
    }

    [Fact]
    public void AClrMemberStandardDoesNotDeclare_BecomesAvailable()
    {
        Assert.NotEmpty(MethodsNamed("ToUpper"));
        Assert.NotEmpty(MethodsNamed("Trim"));
    }

    [Fact]
    public void AStaticClrMember_BecomesAvailable()
    {
        Assert.Contains(MethodsNamed("IsNullOrEmpty"), method => method.Modifiers.IsStatic);
    }

    /// <summary>
    /// The collision with no self-healing anywhere downstream: a second Length would make every read
    /// of it MIST_PM_AMBIGUOUS_MEMBER, because member lookup dedupes overloads by signature and
    /// properties not at all. A green result here is the difference between widening the type and
    /// breaking the one member of it that already worked.
    /// </summary>
    [Fact]
    public void ADeclaredProperty_IsNotDuplicated()
    {
        _ = Assert.Single(stringType.PropertiesList, property => property.Name == "Length");
    }

    /// <summary>
    /// Clone collides on signature while DIFFERING in return type -- Standard returns String where
    /// the CLR returns Object. Overload keys exclude the return type, so exactly one survives, and
    /// which one is not a detail: keeping the CLR member would retype every s.Clone() as Object and
    /// leave String's IClonable conformance answered by the wrong method.
    /// </summary>
    [Fact]
    public void ADeclaredMethodWins_OverTheClrMemberItCollidesWith()
    {
        var clone = Assert.Single(MethodsNamed("Clone"));

        Assert.Equal("Standard.String", clone.ReturnType.FullyQualifiedName);
        Assert.True(clone.Modifiers.IsIntrinsic);
    }

    /// <summary>
    /// The reason the merge keys on the whole signature rather than on the name. Standard declares
    /// one Substring; the CLR declares another taking two arguments, and skipping by name would
    /// silently lose it.
    /// </summary>
    [Fact]
    public void ADeclaredOverload_DoesNotSuppressTheOthers()
    {
        var arities = MethodsNamed("Substring").Select(method => method.ParametersList.Count).ToHashSet();

        Assert.Contains(1, arities);
        Assert.Contains(2, arities);
    }

    [Fact]
    public void AWellKnownTypeThatIsNotBridged_GainsNothing()
    {
        var int32 = (ConcreteType)programModel.TypesByFQN["Standard.Int32"];

        Assert.Empty(int32.MethodsList);
        Assert.Empty(int32.PropertiesList);
    }
}