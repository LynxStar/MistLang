using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ClrEnumConstantProjectionTests
{
    private static readonly Dictionary<string, IMistType> Primitives = new()
    {
        ["System.SByte"] = TypeProjector.CreateShell(typeof(sbyte), "System"),
        ["System.Byte"] = TypeProjector.CreateShell(typeof(byte), "System"),
        ["System.Int16"] = TypeProjector.CreateShell(typeof(short), "System"),
        ["System.UInt16"] = TypeProjector.CreateShell(typeof(ushort), "System"),
        ["System.Int32"] = TypeProjector.CreateShell(typeof(int), "System"),
        ["System.UInt32"] = TypeProjector.CreateShell(typeof(uint), "System"),
        ["System.Int64"] = TypeProjector.CreateShell(typeof(long), "System"),
        ["System.UInt64"] = TypeProjector.CreateShell(typeof(ulong), "System"),
    };

    [Theory]
    [InlineData(typeof(SByteBounds), "System.SByte", sbyte.MinValue, sbyte.MaxValue)]
    [InlineData(typeof(ByteBounds), "System.Byte", byte.MinValue, byte.MaxValue)]
    [InlineData(typeof(Int16Bounds), "System.Int16", short.MinValue, short.MaxValue)]
    [InlineData(typeof(UInt16Bounds), "System.UInt16", ushort.MinValue, ushort.MaxValue)]
    [InlineData(typeof(Int32Bounds), "System.Int32", int.MinValue, int.MaxValue)]
    [InlineData(typeof(UInt32Bounds), "System.UInt32", uint.MinValue, uint.MaxValue)]
    [InlineData(typeof(Int64Bounds), "System.Int64", long.MinValue, long.MaxValue)]
    [InlineData(typeof(UInt64Bounds), "System.UInt64", ulong.MinValue, ulong.MaxValue)]
    public void EveryIntegralBase_KeepsBothExtremesAsItsOwnPrimitive(Type fixture, string underlying, object min, object max)
    {
        var projected = Project(fixture);

        Assert.Same(Primitives[underlying], projected.UnderlyingType);
        Assert.Equal(new Dictionary<string, object> { ["Min"] = min, ["Max"] = max }, Constants(fixture, projected));
    }

    [Fact]
    public void SparseNegativeAndAliasedConstants_AreReadNotCounted()
    {
        var projected = Project(typeof(SparseCodes));

        Assert.Equal(
            new Dictionary<string, object>
            {
                ["Negative"] = -3,
                ["Counted"] = -2,
                ["Zero"] = 0,
                ["Far"] = 1000,
                ["AliasOfFar"] = 1000,
            },
            Constants(typeof(SparseCodes), projected));
    }

    [Fact]
    public void Flags_KeepTheirHighBitAndGainNoUndeclaredCombination()
    {
        var projected = Project(typeof(WideFlags));

        Assert.Equal(
            new Dictionary<string, object>
            {
                ["None"] = 0UL,
                ["Low"] = 1UL,
                ["Middle"] = 1UL << 32,
                ["High"] = 1UL << 63,
                ["LowAndHigh"] = (1UL << 63) | 1UL,
            },
            Constants(typeof(WideFlags), projected));
    }

    private static EnumType Project(Type fixture)
    {
        // Read through a MetadataLoadContext, as the provider reads every assembly: nothing loaded there
        // can run or be initialized, so a projection that tried either throws here instead of passing.
        using var metadata = AmbientProviderTestHelper.RuntimeResolvingContext(fixture.Assembly.Location);
        var clrType = metadata.LoadFromAssemblyPath(fixture.Assembly.Location).GetType(fixture.FullName!)!;
        var shell = Assert.IsType<EnumType>(TypeProjector.CreateShell(clrType, clrType.Namespace!));
        var resolver = new AmbientTypeResolver(
            new Dictionary<string, DeclaredType> { [clrType.FullName!] = shell },
            Primitives,
            new TypeInterner(),
            new DiagnosticBag());

        EnumProjector.Project(shell, clrType, resolver);

        return shell;
    }

    private static Dictionary<string, object> Constants(Type fixture, EnumType projected)
    {
        return projected.MembersList.ToDictionary(
            member => member.Name,
            member =>
            {
                var constant = Assert.IsType<LiteralExpression>(member.Value);
                Assert.Same(projected.UnderlyingType, constant.Type);
                Assert.IsType(fixture.GetEnumUnderlyingType(), constant.Value);
                return constant.Value;
            });
    }
}