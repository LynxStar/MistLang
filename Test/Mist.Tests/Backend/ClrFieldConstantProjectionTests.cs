using Mist.Backends.CSharpTranspiler.AmbientProvider;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.InteropFixtures;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class ClrFieldConstantProjectionTests
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
        ["System.Char"] = TypeProjector.CreateShell(typeof(char), "System"),
        ["System.Boolean"] = TypeProjector.CreateShell(typeof(bool), "System"),
        ["System.Single"] = TypeProjector.CreateShell(typeof(float), "System"),
        ["System.Double"] = TypeProjector.CreateShell(typeof(double), "System"),
        ["System.String"] = TypeProjector.CreateShell(typeof(string), "System"),
        ["System.Void"] = TypeProjector.CreateShell(typeof(void), "System"),
    };

    [Theory]
    [InlineData(nameof(FieldConstants.SByteValue), "System.SByte", sbyte.MinValue)]
    [InlineData(nameof(FieldConstants.ByteValue), "System.Byte", byte.MaxValue)]
    [InlineData(nameof(FieldConstants.Int16Value), "System.Int16", (short)-300)]
    [InlineData(nameof(FieldConstants.UInt16Value), "System.UInt16", ushort.MaxValue)]
    [InlineData(nameof(FieldConstants.Int32Value), "System.Int32", -42)]
    [InlineData(nameof(FieldConstants.UInt32Value), "System.UInt32", uint.MaxValue)]
    [InlineData(nameof(FieldConstants.Int64Value), "System.Int64", long.MinValue)]
    [InlineData(nameof(FieldConstants.UInt64Value), "System.UInt64", ulong.MaxValue)]
    [InlineData(nameof(FieldConstants.CharValue), "System.Char", 'M')]
    [InlineData(nameof(FieldConstants.BoolValue), "System.Boolean", true)]
    [InlineData(nameof(FieldConstants.SingleValue), "System.Single", -1.5f)]
    [InlineData(nameof(FieldConstants.DoubleValue), "System.Double", 2.25)]
    [InlineData(nameof(FieldConstants.StringValue), "System.String", "mist")]
    public void LiteralFields_CarryTheirMetadataValueAsTheirOwnPrimitive(string name, string clrType, object expected)
    {
        var field = Project()[name];

        var constant = Assert.IsType<LiteralExpression>(field.Initializer);
        Assert.Same(Primitives[clrType], constant.Type);
        Assert.IsType(expected.GetType(), constant.Value);
        Assert.Equal(expected, constant.Value);
        Assert.True(field.Modifiers.IsReadonly);
        Assert.True(field.Modifiers.IsStatic);
    }

    [Theory]
    [InlineData(nameof(FieldConstants.ReadonlyValue), true)]
    [InlineData(nameof(FieldConstants.MutableValue), false)]
    [InlineData(nameof(FieldConstants.NullString), true)]
    [InlineData(nameof(FieldConstants.EnumValue), true)]
    public void FieldsWithoutAnHonestLiteral_CarryNoValue(string name, bool isReadonly)
    {
        var field = Project()[name];

        Assert.Null(field.Initializer);
        Assert.Equal(isReadonly, field.Modifiers.IsReadonly);
    }

    private static Dictionary<string, Field> Project()
    {
        // Read through a MetadataLoadContext, as the provider reads every assembly: a static readonly
        // value exists only once the type initializer runs, which nothing loaded there can do.
        var fixture = typeof(FieldConstants);
        using var metadata = AmbientProviderTestHelper.RuntimeResolvingContext(fixture.Assembly.Location);
        var assembly = metadata.LoadFromAssemblyPath(fixture.Assembly.Location);
        var clrType = assembly.GetType(fixture.FullName!)!;
        var enumType = assembly.GetType(typeof(SparseCodes).FullName!)!;
        var shell = Assert.IsType<ClassType>(TypeProjector.CreateShell(clrType, clrType.Namespace!));
        var resolver = new AmbientTypeResolver(
            new Dictionary<string, DeclaredType>
            {
                [clrType.FullName!] = shell,
                [enumType.FullName!] = TypeProjector.CreateShell(enumType, enumType.Namespace!),
            },
            Primitives,
            new TypeInterner(),
            new DiagnosticBag());

        FieldProjector.Project(shell, clrType, resolver);

        return shell.FieldsList.ToDictionary(field => field.Name);
    }
}