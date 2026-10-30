using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class DiagnosticParameterSnapshotTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TypeOwnedParametersRetainDeclaringAssemblyIdentity(bool isPack, bool useProjectPath)
    {
        var first = TypeParameter(Owner("First", useProjectPath), isPack);
        var second = TypeParameter(Owner("Second", useProjectPath), isPack);
        var equivalent = TypeParameter(Owner("First", useProjectPath), isPack);

        var snapshot = DiagnosticSnapshot.Type(first);

        Assert.Same(MistAssembly.Synthetic, first.Assembly);
        Assert.Equal(first.FullyQualifiedName, second.FullyQualifiedName);
        Assert.Equal(isPack ? "TArgs" : "T", snapshot.Spelling);
        Assert.Equal(snapshot.Spelling, DiagnosticSnapshot.Type(second).Spelling);
        Assert.NotEqual(snapshot.Identity, DiagnosticSnapshot.Type(second).Identity);
        Assert.Equal(snapshot, DiagnosticSnapshot.Type(equivalent));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeOwnedParametersWithEqualSpellingDistinguishOrdinals(bool isPack)
    {
        var owner = Owner("First");
        var first = TypeParameter(owner, isPack, 0);
        var second = TypeParameter(owner, isPack, 1);

        Assert.Equal(DiagnosticSnapshot.Type(first).Spelling, DiagnosticSnapshot.Type(second).Spelling);
        Assert.NotEqual(DiagnosticSnapshot.Type(first).Identity, DiagnosticSnapshot.Type(second).Identity);
        Assert.Equal(DiagnosticSnapshot.Type(first), DiagnosticSnapshot.Type(TypeParameter(owner, isPack, 0)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeOwnedPackElementsRetainDeclaringAssemblyIdentity(bool useProjectPath)
    {
        var first = TypeParameter(Owner("First", useProjectPath), true).ElementAt(0);
        var second = TypeParameter(Owner("Second", useProjectPath), true).ElementAt(0);
        var equivalent = TypeParameter(Owner("First", useProjectPath), true).ElementAt(0);

        var snapshot = DiagnosticSnapshot.Type(first);

        Assert.Equal("TArgs[0]", snapshot.Spelling);
        Assert.Equal(first.FullyQualifiedName, second.FullyQualifiedName);
        Assert.Equal(snapshot.Spelling, DiagnosticSnapshot.Type(second).Spelling);
        Assert.NotEqual(snapshot.Identity, DiagnosticSnapshot.Type(second).Identity);
        Assert.Equal(snapshot, DiagnosticSnapshot.Type(equivalent));
    }

    [Fact]
    public void TypeOwnedPackElementsDistinguishEqualSpellingPacksByOrdinal()
    {
        var owner = Owner("First");
        var first = TypeParameter(owner, true, 0).ElementAt(0);
        var second = TypeParameter(owner, true, 1).ElementAt(0);

        Assert.Equal(DiagnosticSnapshot.Type(first).Spelling, DiagnosticSnapshot.Type(second).Spelling);
        Assert.NotEqual(DiagnosticSnapshot.Type(first).Identity, DiagnosticSnapshot.Type(second).Identity);
    }

    [Fact]
    public void MethodOwnedPacksAndElementsWithEqualSpellingDistinguishOrdinals()
    {
        var method = MethodPack(Owner("First"));
        var first = method.TypeParametersList[0];
        var second = AddMethodParameter(method, "TArgs", true);

        Assert.Equal(DiagnosticSnapshot.Type(first).Spelling, DiagnosticSnapshot.Type(second).Spelling);
        Assert.NotEqual(DiagnosticSnapshot.Type(first).Identity, DiagnosticSnapshot.Type(second).Identity);
        Assert.Equal(DiagnosticSnapshot.Type(first.ElementAt(0)).Spelling, DiagnosticSnapshot.Type(second.ElementAt(0)).Spelling);
        Assert.NotEqual(DiagnosticSnapshot.Type(first.ElementAt(0)).Identity, DiagnosticSnapshot.Type(second.ElementAt(0)).Identity);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackElementsDistinguishIndices(bool methodOwned)
    {
        var pack = methodOwned ? MethodPack(Owner("First")).TypeParametersList[0] : TypeParameter(Owner("First"), true);
        var first = DiagnosticSnapshot.Type(pack.ElementAt(0));
        var second = DiagnosticSnapshot.Type(pack.ElementAt(1));

        Assert.Equal("TArgs[0]", first.Spelling);
        Assert.Equal("TArgs[1]", second.Spelling);
        Assert.NotEqual(first.Identity, second.Identity);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("arity")]
    public void MethodPacksAndElementsRetainDeclaringSignature(string difference)
    {
        var first = MethodPack(Owner("First"));
        var second = MethodPack(Owner("First"), difference == "name" ? "Other" : "Run");
        var equivalent = MethodPack(Owner("First"));
        if(difference == "arity")
        {
            _ = AddMethodParameter(second, "U");
        }

        AssertDistinctPacksAndElements(first, second, equivalent);
    }

    [Theory]
    [InlineData(true, false, OwnershipKind.Unrestricted, ByRefDirection.None)]
    [InlineData(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref)]
    [InlineData(true, true, OwnershipKind.Unrestricted, ByRefDirection.In)]
    [InlineData(false, true, OwnershipKind.Unrestricted, ByRefDirection.Out)]
    [InlineData(false, false, OwnershipKind.Owned, ByRefDirection.None)]
    [InlineData(false, false, OwnershipKind.Borrowed, ByRefDirection.None)]
    [InlineData(false, false, OwnershipKind.Frozen, ByRefDirection.None)]
    public void MethodPacksAndElementsRetainSignaturePassingModes(
        bool isReadonly, bool isRef, OwnershipKind ownership, ByRefDirection direction)
    {
        var owner = Owner("First");
        var first = MethodPack(owner);
        var second = MethodPack(owner);
        var equivalent = MethodPack(owner);
        first.ParametersList.Add(new Parameter("value", owner));
        second.ParametersList.Add(new Parameter("value", owner, new ParameterModifier(isReadonly, isRef, ownership, direction)));
        equivalent.ParametersList.Add(new Parameter("value", owner));

        AssertDistinctPacksAndElements(first, second, equivalent);
    }

    [Fact]
    public void MethodPacksAndElementsDistinguishByRefDirectionsWithEqualDeclarationModifiers()
    {
        var owner = Owner("First");
        var first = MethodPack(owner);
        var second = MethodPack(owner);
        var equivalent = MethodPack(owner);
        first.ParametersList.Add(new Parameter("value", owner, new ParameterModifier(true, true, OwnershipKind.Unrestricted, ByRefDirection.Ref)));
        second.ParametersList.Add(new Parameter("value", owner, new ParameterModifier(true, true, OwnershipKind.Unrestricted, ByRefDirection.In)));
        equivalent.ParametersList.Add(new Parameter("value", owner, new ParameterModifier(true, true, OwnershipKind.Unrestricted, ByRefDirection.Ref)));

        AssertDistinctPacksAndElements(first, second, equivalent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MethodPacksAndElementsRetainDeclaringAssemblyIdentity(bool useProjectPath)
    {
        AssertDistinctPacksAndElements(
            MethodPack(Owner("First", useProjectPath)),
            MethodPack(Owner("Second", useProjectPath)),
            MethodPack(Owner("First", useProjectPath)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MethodPacksAndElementsRetainSignatureTypeAssemblyIdentity(bool useProjectPath)
    {
        var owner = Owner("Host");
        var first = MethodPack(owner);
        var second = MethodPack(owner);
        var equivalent = MethodPack(owner);
        first.ParametersList.Add(new Parameter("value", Owner("First", useProjectPath)));
        second.ParametersList.Add(new Parameter("value", Owner("Second", useProjectPath)));
        equivalent.ParametersList.Add(new Parameter("value", Owner("First", useProjectPath)));

        AssertDistinctPacksAndElements(first, second, equivalent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TypeOwnedParameterSnapshotsRetainReportedIdentityAfterOwnerMutation(bool isPack)
    {
        var owner = Owner("First");
        var parameter = TypeParameter(owner, isPack);
        var before = DiagnosticSnapshot.Type(parameter);
        var elementBefore = isPack ? DiagnosticSnapshot.Type(parameter.ElementAt(0)) : null;

        owner.Assembly = new MistAssembly("Changed", MistAssemblyKind.Reference, "projects/Changed/mist.toml");

        var original = TypeParameter(Owner("First"), isPack);
        Assert.Equal(DiagnosticSnapshot.Type(original), before);
        Assert.NotEqual(before.Identity, DiagnosticSnapshot.Type(parameter).Identity);
        Assert.Equal(before.Spelling, DiagnosticSnapshot.Type(parameter).Spelling);
        if(isPack)
        {
            Assert.Equal(DiagnosticSnapshot.Type(original.ElementAt(0)), elementBefore);
            Assert.NotEqual(elementBefore!.Identity, DiagnosticSnapshot.Type(parameter.ElementAt(0)).Identity);
        }
    }

    [Fact]
    public void MethodPackElementSnapshotsRetainReportedIdentityAfterPackOwnerMutation()
    {
        var owner = Owner("First");
        var method = MethodPack(owner);
        var pack = method.TypeParametersList[0];
        var element = pack.ElementAt(0);
        var packBefore = DiagnosticSnapshot.Type(pack);
        var elementBefore = DiagnosticSnapshot.Type(element);
        var replacement = MethodPack(owner, "Other");

        pack.DeclaringMethod = replacement;

        var original = MethodPack(Owner("First")).TypeParametersList[0];
        Assert.Equal(DiagnosticSnapshot.Type(original), packBefore);
        Assert.Equal(DiagnosticSnapshot.Type(original.ElementAt(0)), elementBefore);
        Assert.NotEqual(packBefore.Identity, DiagnosticSnapshot.Type(pack).Identity);
        Assert.NotEqual(elementBefore.Identity, DiagnosticSnapshot.Type(element).Identity);
        Assert.Equal(elementBefore.Spelling, DiagnosticSnapshot.Type(element).Spelling);
    }

    [Fact]
    public void MethodPackSnapshotsRetainReportedIdentityAfterSignatureMutation()
    {
        var method = MethodPack(Owner("First"));
        var pack = method.TypeParametersList[0];
        var packBefore = DiagnosticSnapshot.Type(pack);
        var elementBefore = DiagnosticSnapshot.Type(pack.ElementAt(0));
        var symbolBefore = DiagnosticSnapshot.Symbol(method, "Run");

        method.ParametersList.Add(new Parameter("value", pack.ElementAt(0)));
        _ = AddMethodParameter(method, "U");

        var original = MethodPack(Owner("First"));
        Assert.Equal(DiagnosticSnapshot.Type(original.TypeParametersList[0]), packBefore);
        Assert.Equal(DiagnosticSnapshot.Type(original.TypeParametersList[0].ElementAt(0)), elementBefore);
        Assert.Equal(DiagnosticSnapshot.Symbol(original, "Run"), symbolBefore);
        Assert.NotEqual(packBefore.Identity, DiagnosticSnapshot.Type(pack).Identity);
        Assert.NotEqual(elementBefore.Identity, DiagnosticSnapshot.Type(pack.ElementAt(0)).Identity);
        Assert.NotEqual(symbolBefore.Identity, DiagnosticSnapshot.Symbol(method, "Run").Identity);
    }

    [Theory]
    [InlineData("constructed", "parameter")]
    [InlineData("enclosing", "parameter")]
    [InlineData("tuple", "parameter")]
    [InlineData("union", "parameter")]
    [InlineData("function", "parameter")]
    [InlineData("constructed", "pack")]
    [InlineData("enclosing", "pack")]
    [InlineData("tuple", "pack")]
    [InlineData("union", "pack")]
    [InlineData("function", "pack")]
    [InlineData("constructed", "element")]
    [InlineData("enclosing", "element")]
    [InlineData("tuple", "element")]
    [InlineData("union", "element")]
    [InlineData("function", "element")]
    public void RecursiveSignaturesRetainNestedTypeOwnedParameterIdentity(string shape, string kind)
    {
        var first = MethodWithTypeOwnedSignature(shape, kind, "First");
        var second = MethodWithTypeOwnedSignature(shape, kind, "Second");
        var equivalent = MethodWithTypeOwnedSignature(shape, kind, "First");
        var snapshot = DiagnosticSnapshot.Type(first.TypeParametersList[0]);
        var symbol = DiagnosticSnapshot.Symbol(first, "Run");

        Assert.NotEqual(snapshot.Identity, DiagnosticSnapshot.Type(second.TypeParametersList[0]).Identity);
        Assert.Equal(snapshot, DiagnosticSnapshot.Type(equivalent.TypeParametersList[0]));
        Assert.NotEqual(symbol.Identity, DiagnosticSnapshot.Symbol(second, "Run").Identity);
        Assert.Equal(symbol, DiagnosticSnapshot.Symbol(equivalent, "Run"));
    }

    [Theory]
    [InlineData("constructed", false)]
    [InlineData("enclosing", false)]
    [InlineData("tuple", false)]
    [InlineData("union", false)]
    [InlineData("function", false)]
    [InlineData("constructed", true)]
    [InlineData("enclosing", true)]
    [InlineData("tuple", true)]
    [InlineData("union", true)]
    [InlineData("function", true)]
    public void RecursiveMethodPackSignaturesTerminateAndRetainNestedAssemblyIdentity(string shape, bool useElement)
    {
        var first = MethodWithPackSignature(shape, useElement, "First");
        var second = MethodWithPackSignature(shape, useElement, "Second");
        var equivalent = MethodWithPackSignature(shape, useElement, "First");

        AssertDistinctPacksAndElements(first, second, equivalent);
        Assert.NotEqual(DiagnosticSnapshot.Symbol(first, "Run").Identity, DiagnosticSnapshot.Symbol(second, "Run").Identity);
        Assert.Equal(DiagnosticSnapshot.Symbol(first, "Run"), DiagnosticSnapshot.Symbol(equivalent, "Run"));
    }

    [Theory]
    [InlineData("constructed")]
    [InlineData("enclosing")]
    [InlineData("tuple")]
    [InlineData("union")]
    [InlineData("function")]
    public void RecursiveMethodPackSignaturesRetainNestedElementIndices(string shape)
    {
        var first = MethodWithPackSignature(shape, true, "First", 0);
        var second = MethodWithPackSignature(shape, true, "First", 1);
        var equivalent = MethodWithPackSignature(shape, true, "First", 0);

        AssertDistinctPacksAndElements(first, second, equivalent);
    }

    private static void AssertDistinctPacksAndElements(Method first, Method second, Method equivalent)
    {
        var firstPack = first.TypeParametersList[0];
        var secondPack = second.TypeParametersList[0];
        var equivalentPack = equivalent.TypeParametersList[0];
        var pack = DiagnosticSnapshot.Type(firstPack);
        var element = DiagnosticSnapshot.Type(firstPack.ElementAt(0));

        Assert.Equal("TArgs", pack.Spelling);
        Assert.Equal(pack.Spelling, DiagnosticSnapshot.Type(secondPack).Spelling);
        Assert.NotEqual(pack.Identity, DiagnosticSnapshot.Type(secondPack).Identity);
        Assert.Equal(pack, DiagnosticSnapshot.Type(equivalentPack));
        Assert.Equal("TArgs[0]", element.Spelling);
        Assert.Equal(element.Spelling, DiagnosticSnapshot.Type(secondPack.ElementAt(0)).Spelling);
        Assert.NotEqual(element.Identity, DiagnosticSnapshot.Type(secondPack.ElementAt(0)).Identity);
        Assert.Equal(element, DiagnosticSnapshot.Type(equivalentPack.ElementAt(0)));
    }

    private static ClassType Owner(string identity, bool useProjectPath = false)
    {
        return new ClassType("Box<>", "Probe", new Scope(null), null, null)
        {
            Assembly = new MistAssembly(useProjectPath ? "Shared" : identity, MistAssemblyKind.Reference,
                useProjectPath ? $"projects/{identity}/mist.toml" : null),
        };
    }

    private static TypeParameter TypeParameter(ClassType owner, bool isPack, int ordinal = 0)
    {
        var parameter = new TypeParameter(isPack ? "TArgs" : "T", ordinal, owner.Scope, owner)
        {
            PackKind = isPack ? TypePackKind.Unbracketed : TypePackKind.None,
        };
        owner.TypeParametersList.Add(parameter);
        return parameter;
    }

    private static Method MethodPack(ClassType owner, string name = "Run")
    {
        var method = Method(owner, name);
        _ = AddMethodParameter(method, "TArgs", true);
        return method;
    }

    private static Method Method(ClassType owner, string name = "Run")
    {
        return new Method(name, owner, [], new MemberModifiers())
        {
            DeclaringType = owner,
            Scope = new MethodScope(owner.Scope),
        };
    }

    private static TypeParameter AddMethodParameter(Method method, string name, bool isPack = false)
    {
        var parameter = new TypeParameter(name, method.TypeParametersList.Count, method.Scope, null)
        {
            DeclaringMethod = method,
            PackKind = isPack ? TypePackKind.Unbracketed : TypePackKind.None,
        };
        method.TypeParametersList.Add(parameter);
        return parameter;
    }

    private static Method MethodWithTypeOwnedSignature(string shape, string kind, string identity)
    {
        var method = Method(Owner("Host"));
        var recursive = AddMethodParameter(method, "TMethod");
        var parameter = TypeParameter(Owner(identity), kind != "parameter");
        var leaf = kind == "element" ? parameter.ElementAt(0) : parameter;
        method.ParametersList.Add(new Parameter("value", SignatureType(shape, recursive, leaf)));
        return method;
    }

    private static Method MethodWithPackSignature(string shape, bool useElement, string identity, int index = 0)
    {
        var method = MethodPack(Owner("Host"));
        var pack = method.TypeParametersList[0];
        var recursive = useElement ? pack.ElementAt(index) : pack;
        method.ParametersList.Add(new Parameter("value", SignatureType(shape, recursive, Owner(identity))));
        return method;
    }

    private static IMistType SignatureType(string shape, IMistType recursive, IMistType leaf)
    {
        var box = Owner("Container");
        return shape switch
        {
            "constructed" => new ConstructedType(box, [recursive, leaf]),
            "enclosing" => new ConstructedType(Owner("Inner"), [recursive], new ConstructedType(box, [leaf, recursive])),
            "tuple" => new TupleType([recursive, leaf]),
            "union" => new UnionType([recursive, leaf]),
            "function" => new FunctionType([recursive, leaf], recursive),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }
}