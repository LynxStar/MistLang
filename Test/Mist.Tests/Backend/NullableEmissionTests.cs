using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class NullableEmissionTests
{
    [Fact]
    [Trait("Tier", "2")]
    public void EnumFallbackKeepsTheNonoptionalMistStringContract()
    {
        var underlying = HandBuiltModel.Struct("Token");
        underlying.Assembly = new MistAssembly("Probe", MistAssemblyKind.Self);
        var enumeration = HandBuiltModel.Enum("Choice", underlying);
        var writer = new CSharpWriter();

        EnumEmitter.Emit(enumeration, writer, new DiagnosticBag());

        Assert.Contains("public override string ToString()", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("return Value.ToString()!;", writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void ReferenceCloneCopyDeclaresItsNullPreservingConversion(bool isImplicit)
    {
        var source = HandBuiltModel.Class("Account");
        var clone = HandBuiltModel.Class("OwnAccount");
        clone.CloneSourceType = source;
        var conversion = new CastOperator(isImplicit ? source : clone,
            new Parameter("value", isImplicit ? clone : source), isImplicit, DeclarationFragments.Public with { IsStatic = true })
        {
            DeclaringType = clone,
            CopiesClone = true,
        };
        var writer = new CSharpWriter();

        CastOperatorEmitter.Emit(conversion, writer, new DiagnosticBag());

        var emitted = writer.ToString();
        Assert.Contains("[return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(\"value\")]", emitted, StringComparison.Ordinal);
        Assert.Contains(isImplicit ? "operator Account?(OwnAccount? value)" : "operator OwnAccount?(Account? value)", emitted, StringComparison.Ordinal);
        Assert.Contains("if(value is null)", emitted, StringComparison.Ordinal);
        Assert.Contains("return null;", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("return null!;", emitted, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void ValueCloneCopyKeepsItsValueSignature()
    {
        var source = HandBuiltModel.Struct("Token");
        var clone = HandBuiltModel.Struct("OwnToken");
        clone.CloneSourceType = source;
        var conversion = new CastOperator(source, new Parameter("value", clone), true, DeclarationFragments.Public with { IsStatic = true })
        {
            DeclaringType = clone,
            CopiesClone = true,
        };
        var writer = new CSharpWriter();

        CastOperatorEmitter.Emit(conversion, writer, new DiagnosticBag());

        Assert.Contains("operator Token(OwnToken value)", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("NotNullIfNotNull", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("is null", writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void UninitializedGenericClassStorageKeepsItsClrZero(bool isStatic)
    {
        var shapes = new GenericDefaultShapes();
        var owner = shapes.Type("Storage");
        var field = new Field("item", GenericDefaultShapes.Parameter(owner), OwnershipKind.Unrestricted,
            DeclarationFragments.Public with { IsStatic = isStatic })
        { DeclaringType = owner };
        var writer = new CSharpWriter();

        FieldEmitter.Emit(field, false, writer, new DiagnosticBag());

        Assert.Contains("[System.Diagnostics.CodeAnalysis.AllowNull]", writer.ToString(), StringComparison.Ordinal);
        Assert.Contains("T item;", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(" = ", writer.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("__default", writer.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Tier", "2")]
    public void UninitializedGenericStructStorageRemainsADeclaration()
    {
        var shapes = new GenericDefaultShapes();
        var owner = shapes.Type("Storage", isStruct: true);
        var parameter = GenericDefaultShapes.Parameter(owner);
        var field = new Field("item", parameter, OwnershipKind.Unrestricted, DeclarationFragments.Public)
        {
            DeclaringType = owner,
        };
        var writer = new CSharpWriter();

        FieldEmitter.Emit(field, false, writer, new DiagnosticBag());

        Assert.Contains("T item;", writer.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(0, "T1")]
    [InlineData(1, "T2")]
    public void IndexedPackStorageKeepsItsSpecializedTypeAndClrZero(int index, string expectedType)
    {
        var owner = HandBuiltModel.Class("Indexed");
        var pack = new TypeParameter("TArgs", 0, owner.Scope, owner) { PackKind = TypePackKind.Unbracketed };
        var name = index == 0 ? "First" : "Second";
        var field = new Field(name, pack.ElementAt(index), OwnershipKind.Unrestricted, DeclarationFragments.Public)
        { DeclaringType = owner };
        var writer = new CSharpWriter();
        using var specialization = Monomorph.For([pack], [2])!.Enter();

        FieldEmitter.Emit(field, false, writer, new DiagnosticBag());

        Assert.Equal($"[System.Diagnostics.CodeAnalysis.AllowNull]\npublic {expectedType} {name};",
            writer.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Trim());
    }

    [Fact]
    [Trait("Tier", "2")]
    public void TupleStorageKeepsItsValueCarrierDeclaration()
    {
        var built = new HandBuiltModel();
        var owner = HandBuiltModel.Class("Storage");
        var parameter = new TypeParameter("T", 0, owner.Scope, owner);
        var field = new Field("item", built.Interner.GetOrCreateTupleType([parameter, parameter]),
            OwnershipKind.Unrestricted, DeclarationFragments.Public)
        { DeclaringType = owner };
        var writer = new CSharpWriter();

        FieldEmitter.Emit(field, false, writer, new DiagnosticBag());

        Assert.Equal("public (T, T) item;", writer.ToString().Trim());
    }

    [Theory]
    [InlineData(TypeParameterConstraintKind.Linear)]
    [InlineData(TypeParameterConstraintKind.Affine)]
    [InlineData(TypeParameterConstraintKind.Relevant)]
    [Trait("Tier", "1")]
    public void GenericDropProvesItsHeldReceiverPresent(TypeParameterConstraintKind kind)
    {
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        parameter.ConstraintsList.Add(new KeywordConstraint(kind));
        var field = new Field("Value", parameter, OwnershipKind.Unrestricted, DeclarationFragments.Public);

        Assert.Equal("((dynamic)this.Value!).Drop();", DropInsertion.FieldDrop(field));
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false, "Standard.UnknownExtensions.GetType(value!)")]
    [InlineData(true, "Standard.UnknownExtensions.GetType(value)")]
    public void UniversalCarrierCallsProveOnlyNonoptionalGenericReceiversPresent(bool optional, string expected)
    {
        var built = new HandBuiltModel();
        var parameter = new TypeParameter("T", 0, new Scope(null), null);
        var receiverType = optional ? built.Interner.GetOrCreateUnionType([parameter, built.Type("Void")]) : parameter;
        var receiver = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", receiverType));
        var block = new ExtensionBlock("self", "Standard", new Scope(null), new ExtensionBlockDeclarationNode())
        {
            ResolvedReceiverType = UnknownType.Instance,
            CarrierName = "UnknownExtensions",
        };
        var method = new Method("GetType", built.Type("Type"), [], DeclarationFragments.Public) { DeclaringBlock = block };
        var access = new MemberAccessExpression(HandBuiltModel.Node(), receiver, "GetType", method.ReturnType);
        var invocation = new InvocationExpression(HandBuiltModel.Node(), access, [], method.ReturnType, method, null);

        Assert.Equal(expected, BodyEmitter.Render(invocation, new DiagnosticBag()));
    }
}