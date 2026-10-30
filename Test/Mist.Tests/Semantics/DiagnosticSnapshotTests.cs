using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class DiagnosticSnapshotTests
{
    [Fact]
    public void CastTargetsWithEqualShortNamesKeepDistinctSymbolIdentities()
    {
        var owner = HandBuiltModel.Class("Source");
        var first = new ClassType("Result", "First", new Scope(null), null, null);
        var second = new ClassType("Result", "Second", new Scope(null), null, null);
        var firstCast = HandBuiltModel.Conversion(owner, owner, first);
        var secondCast = HandBuiltModel.Conversion(owner, owner, second);

        var firstSnapshot = DiagnosticSnapshot.Symbol(firstCast, "cast to Result");
        var secondSnapshot = DiagnosticSnapshot.Symbol(secondCast, "cast to Result");

        Assert.Equal(firstSnapshot.Spelling, secondSnapshot.Spelling);
        Assert.NotEqual(firstSnapshot.Identity, secondSnapshot.Identity);
        Assert.Contains("First.Result", firstSnapshot.Identity, StringComparison.Ordinal);
        Assert.Contains("Second.Result", secondSnapshot.Identity, StringComparison.Ordinal);
    }

    [Fact]
    public void MethodTypeParametersDistinguishGenericArity()
    {
        var owner = HandBuiltModel.Class("Owner");
        var first = GenericMethod(owner, "Run", "T");
        var second = GenericMethod(owner, "Run", "T", "U");
        var equivalent = GenericMethod(owner, "Run", "T");

        var firstSnapshot = DiagnosticSnapshot.Type(first.TypeParametersList[0]);
        var secondSnapshot = DiagnosticSnapshot.Type(second.TypeParametersList[0]);

        Assert.Equal(firstSnapshot.Spelling, secondSnapshot.Spelling);
        Assert.NotEqual(firstSnapshot.Identity, secondSnapshot.Identity);
        Assert.Equal(firstSnapshot, DiagnosticSnapshot.Type(equivalent.TypeParametersList[0]));
    }

    [Fact]
    public void MethodTypeParametersDistinguishMethodNames()
    {
        var owner = HandBuiltModel.Class("Owner");
        var first = GenericMethod(owner, "Run", "T");
        var second = GenericMethod(owner, "Other", "T");

        Assert.NotEqual(DiagnosticSnapshot.Type(first.TypeParametersList[0]).Identity,
            DiagnosticSnapshot.Type(second.TypeParametersList[0]).Identity);
    }

    [Theory]
    [InlineData(true, false, OwnershipKind.Unrestricted, ByRefDirection.None)]
    [InlineData(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref)]
    [InlineData(true, true, OwnershipKind.Unrestricted, ByRefDirection.In)]
    [InlineData(false, true, OwnershipKind.Unrestricted, ByRefDirection.Out)]
    [InlineData(false, false, OwnershipKind.Owned, ByRefDirection.None)]
    [InlineData(false, false, OwnershipKind.Borrowed, ByRefDirection.None)]
    [InlineData(false, false, OwnershipKind.Frozen, ByRefDirection.None)]
    public void MethodTypeParametersDistinguishPassingModes(
        bool isReadonly, bool isRef, OwnershipKind ownership, ByRefDirection direction)
    {
        var owner = HandBuiltModel.Class("Owner");
        var first = GenericMethod(owner, "Run", "T");
        var second = GenericMethod(owner, "Run", "T");
        first.ParametersList.Add(new Parameter("value", owner));
        second.ParametersList.Add(new Parameter("value", owner, new ParameterModifier(isReadonly, isRef, ownership, direction)));

        Assert.NotEqual(DiagnosticSnapshot.Type(first.TypeParametersList[0]).Identity,
            DiagnosticSnapshot.Type(second.TypeParametersList[0]).Identity);
    }

    [Fact]
    public void MethodTypeParametersDistinguishByRefDirectionsWithEqualDeclarationModifiers()
    {
        var owner = HandBuiltModel.Class("Owner");
        var first = GenericMethod(owner, "Run", "T");
        var second = GenericMethod(owner, "Run", "T");
        first.ParametersList.Add(new Parameter("value", owner, new ParameterModifier(true, true, OwnershipKind.Unrestricted, ByRefDirection.Ref)));
        second.ParametersList.Add(new Parameter("value", owner, new ParameterModifier(true, true, OwnershipKind.Unrestricted, ByRefDirection.In)));

        Assert.NotEqual(DiagnosticSnapshot.Type(first.TypeParametersList[0]).Identity,
            DiagnosticSnapshot.Type(second.TypeParametersList[0]).Identity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MethodTypeParametersDistinguishDeclaringAssemblies(bool useProjectPath)
    {
        var firstOwner = AssemblyType("Owner", "First", useProjectPath);
        var secondOwner = AssemblyType("Owner", "Second", useProjectPath);
        var first = GenericMethod(firstOwner, "Run", "T");
        var second = GenericMethod(secondOwner, "Run", "T");

        Assert.Equal(firstOwner.FullyQualifiedName, secondOwner.FullyQualifiedName);
        Assert.NotEqual(DiagnosticSnapshot.Type(first.TypeParametersList[0]).Identity,
            DiagnosticSnapshot.Type(second.TypeParametersList[0]).Identity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MethodTypeParametersDistinguishSignatureTypeAssemblies(bool useProjectPath)
    {
        var owner = HandBuiltModel.Class("Owner");
        var first = GenericMethod(owner, "Run", "T");
        var second = GenericMethod(owner, "Run", "T");
        first.ParametersList.Add(new Parameter("value", AssemblyType("Value", "First", useProjectPath)));
        second.ParametersList.Add(new Parameter("value", AssemblyType("Value", "Second", useProjectPath)));

        Assert.Equal(first.ParametersList[0].Type.FullyQualifiedName, second.ParametersList[0].Type.FullyQualifiedName);
        Assert.NotEqual(DiagnosticSnapshot.Type(first.TypeParametersList[0]).Identity,
            DiagnosticSnapshot.Type(second.TypeParametersList[0]).Identity);
    }

    [Theory]
    [InlineData("constructed")]
    [InlineData("enclosing")]
    [InlineData("tuple")]
    [InlineData("union")]
    [InlineData("function")]
    public void RecursiveMethodSignaturesRetainNestedAssemblyIdentity(string shape)
    {
        var owner = HandBuiltModel.Class("Owner");
        var first = GenericMethod(owner, "Run", "T");
        var second = GenericMethod(owner, "Run", "T");
        var equivalent = GenericMethod(owner, "Run", "T");
        first.ParametersList.Add(new Parameter("value", SignatureType(shape, first.TypeParametersList[0], AssemblyType("Value", "First", false))));
        second.ParametersList.Add(new Parameter("value", SignatureType(shape, second.TypeParametersList[0], AssemblyType("Value", "Second", false))));
        equivalent.ParametersList.Add(new Parameter("value", SignatureType(shape, equivalent.TypeParametersList[0], AssemblyType("Value", "First", false))));

        var firstSnapshot = DiagnosticSnapshot.Type(first.TypeParametersList[0]);

        Assert.NotEqual(firstSnapshot.Identity, DiagnosticSnapshot.Type(second.TypeParametersList[0]).Identity);
        Assert.Equal(firstSnapshot, DiagnosticSnapshot.Type(equivalent.TypeParametersList[0]));
    }

    [Fact]
    public void RecursiveMethodSignaturesDistinguishGenericParameterPositions()
    {
        var owner = HandBuiltModel.Class("Owner");
        var first = GenericMethod(owner, "Run", "T", "U");
        var second = GenericMethod(owner, "Run", "T", "U");
        first.ParametersList.Add(new Parameter("value", first.TypeParametersList[0]));
        second.ParametersList.Add(new Parameter("value", second.TypeParametersList[1]));

        Assert.NotEqual(DiagnosticSnapshot.Type(first.TypeParametersList[0]).Identity,
            DiagnosticSnapshot.Type(second.TypeParametersList[0]).Identity);
    }

    [Fact]
    public void MethodTypeParametersWithEqualNamesDistinguishOrdinals()
    {
        var owner = HandBuiltModel.Class("Owner");
        var first = GenericMethod(owner, "Run", "T", "U");
        var second = GenericMethod(owner, "Run", "U", "T");

        var firstSnapshot = DiagnosticSnapshot.Type(first.TypeParametersList[0]);
        var secondSnapshot = DiagnosticSnapshot.Type(second.TypeParametersList[1]);

        Assert.Equal(firstSnapshot.Spelling, secondSnapshot.Spelling);
        Assert.NotEqual(firstSnapshot.Identity, secondSnapshot.Identity);
    }

    [Fact]
    public void MethodTypeParameterSnapshotsRetainReportedSignatureAfterMutation()
    {
        var owner = HandBuiltModel.Class("Owner");
        var method = GenericMethod(owner, "Run", "T");
        var before = DiagnosticSnapshot.Type(method.TypeParametersList[0]);

        method.ParametersList.Add(new Parameter("value", method.TypeParametersList[0]));
        method.TypeParametersList.Add(new TypeParameter("U", 1, method.Scope, null) { DeclaringMethod = method });
        owner.Assembly = new MistAssembly("Changed", MistAssemblyKind.Reference);

        var originalOwner = HandBuiltModel.Class("Owner");
        var original = GenericMethod(originalOwner, "Run", "T");

        Assert.Equal(DiagnosticSnapshot.Type(original.TypeParametersList[0]), before);
        Assert.NotEqual(before.Identity, DiagnosticSnapshot.Type(method.TypeParametersList[0]).Identity);
    }

    private static Method GenericMethod(ClassType owner, string name, params string[] parameterNames)
    {
        var method = new Method(name, owner, [], new MemberModifiers())
        {
            DeclaringType = owner,
            Scope = new MethodScope(owner.Scope),
        };
        foreach(var parameterName in parameterNames)
        {
            method.TypeParametersList.Add(new TypeParameter(parameterName, method.TypeParametersList.Count, method.Scope, null)
            {
                DeclaringMethod = method,
            });
        }

        return method;
    }

    private static ClassType AssemblyType(string name, string identity, bool useProjectPath)
    {
        return new ClassType(name, "Probe", new Scope(null), null, null)
        {
            Assembly = new MistAssembly(useProjectPath ? "Shared" : identity, MistAssemblyKind.Reference,
                useProjectPath ? $"projects/{identity}/mist.toml" : null),
        };
    }

    private static IMistType SignatureType(string shape, TypeParameter parameter, IMistType leaf)
    {
        var box = HandBuiltModel.Class("Box<,>");
        return shape switch
        {
            "constructed" => new ConstructedType(box, [parameter, leaf]),
            "enclosing" => new ConstructedType(HandBuiltModel.Class("Inner<>"), [parameter], new ConstructedType(box, [leaf, parameter])),
            "tuple" => new TupleType([parameter, leaf]),
            "union" => new UnionType([parameter, leaf]),
            "function" => new FunctionType([parameter], leaf),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }
}