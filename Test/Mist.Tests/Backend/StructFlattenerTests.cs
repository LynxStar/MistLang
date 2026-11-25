using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

/// <summary>
/// The decisions <see cref="StructFlattener"/> makes while folding a struct's ancestors into it:
/// which inherited members survive, how they are re-spelled for the absorbing struct, and when the
/// chain is refused. Every struct is hand-built, so nothing upstream of the backend runs.
/// </summary>
[Trait("Tier", "2")]
public sealed class StructFlattenerTests
{
    private static readonly StructType Int32 = Intrinsic("Int32");
    private static readonly StructType Int64 = Intrinsic("Int64");

    private readonly DiagnosticBag diagnostics = new(PipelineStage.Backend);

    [Fact]
    public void AFieldRedeclaredBelowAnAncestor_IsRefusedAtTheRedeclaringStruct_NamingTheStructThatDeclaredItFirst()
    {
        var root = Struct("Root", offset: 3);
        _ = AddField(root, "reading");
        var middle = Struct("Middle", root, offset: 11);
        _ = AddField(middle, "dial");
        var leaf = Struct("Leaf", middle, offset: 17);
        _ = AddField(leaf, "reading");

        _ = Flatten(leaf);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(BackendDiagnostics.StructInheritanceFieldCollision.Number, refusal.Number);
        Assert.Equal(17, Assert.IsType<SourceRange>(refusal.Location).Offset);
        Assert.Equal("reading", Assert.IsType<NameValue>(refusal.Arguments[0]).Name);

        // The two structs, not their order: the descriptor's slots name the redeclaring struct and
        // its ancestor, and which of them each slot receives is not settled here.
        var structs = refusal.Arguments.Skip(1).Select(argument => Assert.IsType<TypeValue>(argument).Spelling).Order(StringComparer.Ordinal);
        Assert.Equal(["Leaf", "Root"], structs);
    }

    [Fact]
    public void DistinctFieldNamesAcrossTheChain_AreFlattenedRootFirstWithoutARefusal()
    {
        var root = Struct("Root");
        var reading = AddField(root, "reading");
        var middle = Struct("Middle", root);
        var dial = AddField(middle, "dial");
        var leaf = Struct("Leaf", middle);
        var upper = AddField(leaf, "Reading");
        var face = AddField(leaf, "face");

        var flattened = Flatten(leaf);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal([reading, dial, upper, face], flattened.Fields.Select(field => field.Field));
        Assert.Equal([root, middle, leaf, leaf], flattened.Fields.Select(field => field.DeclaringStruct));
    }

    [Fact]
    public void AnInheritedMethod_IsDroppedOnlyWhereTheDescendantRedeclaresItsParameterTypes()
    {
        var root = Struct("Root");
        var area = AddMethod(root, "Area", Int32);
        var scaleNarrow = AddMethod(root, "Scale", Int32, new Parameter("amount", Int32));
        var scaleWide = AddMethod(root, "Scale", Int32, new Parameter("amount", Int64));
        var leaf = Struct("Leaf", root);
        var leafArea = AddMethod(leaf, "Area", Int64);
        var leafScale = AddMethod(leaf, "Scale", Int64, new Parameter("factor", Int64));

        var flattened = Flatten(leaf);

        // Area differs only in return type and Scale(Int64) only in its parameter's name, so both
        // are redeclarations; Scale(Int32) differs in a parameter type, so it is an overload that
        // survives beside them.
        Assert.Equal<Method>([scaleNarrow, leafArea, leafScale], flattened.Methods);
        Assert.DoesNotContain(area, flattened.Methods);
        Assert.DoesNotContain(scaleWide, flattened.Methods);
    }

    [Fact]
    public void AnInheritedProperty_IsDroppedWhereverTheDescendantRedeclaresItsName()
    {
        var root = Struct("Root");
        var label = AddProperty(root, "Label", Int32, Settable());
        var size = AddProperty(root, "Size", Int32, Settable());
        var leaf = Struct("Leaf", root);
        var leafSize = AddProperty(leaf, "Size", Int64, Settable());

        var flattened = Flatten(leaf);

        Assert.Equal<Property>([label, leafSize], flattened.Properties);
        Assert.DoesNotContain(size, flattened.Properties);
    }

    [Theory]
    [InlineData(Accessibility.Public, Accessibility.Public)]
    [InlineData(Accessibility.Private, Accessibility.Private)]
    [InlineData(Accessibility.Internal, Accessibility.Internal)]
    [InlineData(Accessibility.Protected, Accessibility.Private)]
    public void FieldAccessibility_IsKeptExceptThatProtectedBecomesPrivate_OnInheritedAndOwnFields(
        Accessibility declared, Accessibility emitted)
    {
        var root = Struct("Root");
        _ = AddField(root, "reading", declared);
        var leaf = Struct("Leaf", root);
        _ = AddField(leaf, "face", declared);

        var flattened = Flatten(leaf);

        Assert.Equal([emitted, emitted], flattened.Fields.Select(field => field.Accessibility));
    }

    [Fact]
    public void AnInheritedGetOnlyOrInitProperty_GainsAPrivateSetter_WhileASettableOneIsAbsorbedAsDeclared()
    {
        var root = Struct("Root");
        var getter = new PropertyAccessor { IsAuto = true, Accessibility = Accessibility.Public };
        var getOnly = AddProperty(root, "Mass", Int32, setter: null, getter);
        getOnly.Initializer = HandBuiltModel.Literal(Int32, 4L);
        var initOnly = AddProperty(root, "Charge", Int64, new PropertyAccessor { IsAuto = true, IsInit = true, Accessibility = Accessibility.Public });
        var settable = AddProperty(root, "Spin", Int32, Settable());
        var leaf = Struct("Leaf", root);

        var flattened = Flatten(leaf);

        Assert.Equal(3, flattened.Properties.Length);
        Assert.Same(settable, flattened.Properties[2]);
        foreach(var (absorbed, declared) in flattened.Properties.Take(2).Zip([getOnly, initOnly]))
        {
            Assert.NotSame(declared, absorbed);
            Assert.Equal((declared.Name, declared.Type, declared.Modifiers), (absorbed.Name, absorbed.Type, absorbed.Modifiers));
            Assert.Same(declared.Getter, absorbed.Getter);
            Assert.Same(declared.Initializer, absorbed.Initializer);
            Assert.NotNull(absorbed.Setter);
            Assert.Equal((true, false, Accessibility.Private), (absorbed.Setter.IsAuto, absorbed.Setter.IsInit, absorbed.Setter.Accessibility));
        }

        Assert.Equal<Property>([getOnly, initOnly, settable], Flatten(root).Properties);
    }

    [Fact]
    public void AConstructedGenericBase_SubstitutesItsArgumentAndRecordsThePathItsMembersWereReachedThrough()
    {
        var root = Struct("Root<>");
        var parameter = AddTypeParameter(root, "T");
        var value = AddField(root, "value", parameter);
        var get = AddMethod(root, "Get", parameter);
        var mass = AddProperty(root, "Mass", parameter, setter: null);
        var closed = new ConstructedType(root, [Int32]);
        var leaf = Struct("Leaf", closed);
        var own = AddMethod(leaf, "Own", Int32);

        var flattened = Flatten(leaf);

        var field = Assert.Single(flattened.Fields);
        Assert.Same(value, field.Field);
        Assert.Same(Int32, field.Type);
        var ancestor = Assert.Single(flattened.Ancestors);
        Assert.Same(root, ancestor.Definition);
        Assert.Same(closed, ancestor.Reference);
        Assert.Equal([closed], ancestor.Path);
        Assert.Equal([closed], flattened.MethodPaths[get]);
        Assert.False(flattened.MethodPaths.ContainsKey(own));
        var absorbed = Assert.Single(flattened.Properties);
        Assert.NotSame(mass, absorbed);
        Assert.Equal([closed], flattened.PropertyPaths[absorbed]);
    }

    [Fact]
    public void AMultiLevelGenericChain_ComposesEachLevelsSubstitution_AndListsAncestorsRootFirstAsTheirDeriverSpelledThem()
    {
        var root = Struct("Root<>");
        var rootParameter = AddTypeParameter(root, "T");
        _ = AddField(root, "value", rootParameter);
        var get = AddMethod(root, "Get", rootParameter);
        var middle = Struct("Middle<>");
        var middleParameter = AddTypeParameter(middle, "U");
        var rootOfU = new ConstructedType(root, [middleParameter]);
        middle.BaseType = rootOfU;
        _ = AddField(middle, "extra", middleParameter);
        var put = AddMethod(middle, "Put", Int32);
        var middleOfInt = new ConstructedType(middle, [Int64]);
        var leaf = Struct("Leaf", middleOfInt);

        var flattened = Flatten(leaf);

        Assert.Equal([Int64, Int64], flattened.Fields.Select(field => field.Type));
        Assert.Collection(
            flattened.Ancestors,
            first =>
            {
                Assert.Same(root, first.Definition);
                Assert.Same(rootOfU, first.Reference);
                Assert.Equal([middleOfInt, rootOfU], first.Path);
            },
            second =>
            {
                Assert.Same(middle, second.Definition);
                Assert.Same(middleOfInt, second.Reference);
                Assert.Equal([middleOfInt], second.Path);
            });
        Assert.Equal([middleOfInt, rootOfU], flattened.MethodPaths[get]);
        Assert.Equal([middleOfInt], flattened.MethodPaths[put]);
    }

    [Fact]
    public void ANonGenericChain_ListsAncestorsRootFirstWithNoPaths()
    {
        var root = Struct("Root");
        var get = AddMethod(root, "Get", Int32);
        var middle = Struct("Middle", root);
        var leaf = Struct("Leaf", middle);

        var flattened = Flatten(leaf);

        Assert.Equal([root, middle], flattened.Ancestors.Select(ancestor => ancestor.Definition));
        Assert.Equal([root, middle], flattened.Ancestors.Select(ancestor => ancestor.Reference));
        Assert.All(flattened.Ancestors, ancestor => Assert.Empty(ancestor.Path));
        Assert.Empty(flattened.MethodPaths);
        Assert.Equal<Method>([get], flattened.Methods);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAbstractBase_IsAConformanceAndNothingIsAbsorbedFromIt(bool constructed)
    {
        var show = new AbstractType(constructed ? "IShow<>" : "IShow", "Probe", new Scope(null), null, null);
        if(constructed)
        {
            show.TypeParametersList.Add(new TypeParameter("T", 0, show.Scope, show));
        }

        show.MethodsList.Add(new Method("Show", Int32, [], DeclarationFragments.Public) { DeclaringType = show });
        var leaf = Struct("Leaf", constructed ? new ConstructedType(show, [Int32]) : show);
        var face = AddField(leaf, "face");

        var flattened = Flatten(leaf);

        Assert.Equal([face], flattened.Fields.Select(field => field.Field));
        Assert.Empty(flattened.Methods);
        Assert.Empty(flattened.Ancestors);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIntrinsicBase_IsReportedOnceAgainstItsDeriver_AndFlatteningDropsItSilently(bool constructed)
    {
        var span = Intrinsic(constructed ? "Span<>" : "Span");
        if(constructed)
        {
            _ = AddTypeParameter(span, "T");
        }

        _ = AddField(span, "length");
        var leaf = Struct("Leaf", constructed ? new ConstructedType(span, [Int32]) : span, offset: 9);
        var face = AddField(leaf, "face");

        var flattened = Flatten(leaf);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal([face], flattened.Fields.Select(field => field.Field));
        Assert.Empty(flattened.Ancestors);

        StructFlattener.ReportIntrinsicBase(leaf, diagnostics);

        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(BackendDiagnostics.IntrinsicStructBase.Number, refusal.Number);
        Assert.Equal(9, Assert.IsType<SourceRange>(refusal.Location).Offset);
        Assert.Equal(["Leaf", span.Name], refusal.Arguments.Select(argument => Assert.IsType<TypeValue>(argument).Spelling));
    }

    [Fact]
    public void AnIntrinsicGrandparent_IsReportedOnlyAgainstTheStructThatNamesIt()
    {
        var span = Intrinsic("Span");
        var middle = Struct("Middle", span, offset: 5);
        var reading = AddField(middle, "reading");
        var leaf = Struct("Leaf", middle, offset: 13);

        StructFlattener.ReportIntrinsicBase(leaf, diagnostics);
        Assert.Empty(diagnostics.ToImmutableArray());

        StructFlattener.ReportIntrinsicBase(middle, diagnostics);
        var refusal = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(5, Assert.IsType<SourceRange>(refusal.Location).Offset);

        var flattened = Flatten(leaf);
        Assert.Equal([middle], flattened.Ancestors.Select(ancestor => ancestor.Definition));
        Assert.Equal([reading], flattened.Fields.Select(field => field.Field));
    }

    private FlattenedStruct Flatten(StructType structType)
    {
        return new StructFlattener(diagnostics).Flatten(structType);
    }

    private static StructType Struct(string name, MistType? baseType = null, int offset = 0)
    {
        return new StructType(name, "Probe", new Scope(null), null, BindingFragments.At<BodyTypeTypeDeclarationNode>(offset))
        {
            BaseType = baseType,
            Modifiers = DeclarationFragments.Public,
        };
    }

    private static StructType Intrinsic(string name)
    {
        return new StructType(name, "Standard", new Scope(null), null, null)
        {
            Modifiers = DeclarationFragments.Public with { IsIntrinsic = true },
        };
    }

    private static TypeParameter AddTypeParameter(StructType owner, string name)
    {
        var parameter = new TypeParameter(name, owner.TypeParametersList.Count, owner.Scope, owner);
        owner.TypeParametersList.Add(parameter);
        return parameter;
    }

    private static Field AddField(StructType owner, string name, Accessibility accessibility = Accessibility.Public)
    {
        return AddField(owner, name, Int32, accessibility);
    }

    private static Field AddField(StructType owner, string name, IMistType type, Accessibility accessibility = Accessibility.Public)
    {
        var field = new Field(name, type, OwnershipKind.Unrestricted, DeclarationFragments.Public with { Accessibility = accessibility })
        {
            DeclaringType = owner,
        };
        owner.FieldsList.Add(field);
        return field;
    }

    private static Method AddMethod(StructType owner, string name, IMistType returnType, params Parameter[] parameters)
    {
        var method = new Method(name, returnType, [.. parameters], DeclarationFragments.Public) { DeclaringType = owner };
        owner.MethodsList.Add(method);
        return method;
    }

    private static Property AddProperty(StructType owner, string name, IMistType type, PropertyAccessor? setter, PropertyAccessor? getter = null)
    {
        var property = new Property(name, type, DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Getter = getter ?? new PropertyAccessor { IsAuto = true, Accessibility = Accessibility.Public },
            Setter = setter,
        };
        owner.PropertiesList.Add(property);
        return property;
    }

    private static PropertyAccessor Settable()
    {
        return new PropertyAccessor { IsAuto = true, Accessibility = Accessibility.Public };
    }
}