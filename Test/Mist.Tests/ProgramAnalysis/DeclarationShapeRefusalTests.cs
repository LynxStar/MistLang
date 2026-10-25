using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 2: declaration checks over directly constructed members and inheritance paths.
[Trait("Tier", "2")]
public sealed class DeclarationShapeRefusalTests
{
    [Theory]
    [InlineData("default-entry-not-a-field", "MIST0168", 1)]
    [InlineData("default-of-non-defaultable", "MIST0180", 1)]
    [InlineData("duplicate-abstract-method-signature", "MIST0176", 1)]
    [InlineData("duplicate-constructor-signature", "MIST0176", 1)]
    [InlineData("duplicate-generic-method-over-a-union-its-type-parameter-sorts-into", "MIST0176", 1)]
    [InlineData("duplicate-generic-method-signature", "MIST0176", 1)]
    [InlineData("duplicate-indexer-signature", "MIST0176", 1)]
    [InlineData("duplicate-method-declared-three-times", "MIST0176", 2)]
    [InlineData("duplicate-method-differing-only-in-ownership", "MIST0176", 1)]
    [InlineData("duplicate-method-differing-only-in-params", "MIST0176", 1)]
    [InlineData("duplicate-method-differing-only-in-readonly-ref", "MIST0176", 1)]
    [InlineData("duplicate-method-differing-only-in-return-type", "MIST0176", 1)]
    [InlineData("duplicate-method-differing-only-in-tuple-element-names", "MIST0176", 1)]
    [InlineData("duplicate-method-over-a-refinement-of-the-other-parameter", "MIST0176", 1)]
    [InlineData("duplicate-method-over-two-spellings-of-an-optional", "MIST0176", 1)]
    [InlineData("duplicate-method-signature", "MIST0176", 1)]
    [InlineData("duplicate-method-static-against-instance", "MIST0176", 1)]
    [InlineData("enum-base-not-readonly", "MIST0166", 1)]
    [InlineData("enum-duplicate-value", "MIST0167", 1)]
    [InlineData("enum-union-constituent-not-readonly", "MIST0166", 1)]
    [InlineData("field-redeclared-from-a-base", "MIST0177", 1)]
    [InlineData("field-redeclared-over-an-inherited-method", "MIST0177", 1)]
    [InlineData("indexer-redeclared-by-closing-a-generic-base", "MIST0177", 1)]
    [InlineData("indexer-redeclared-from-a-base", "MIST0177", 1)]
    [InlineData("logical-operator-declared", "MIST0171", 1)]
    [InlineData("method-redeclared-by-closing-a-generic-base", "MIST0177", 1)]
    [InlineData("method-redeclared-from-a-base", "MIST0177", 1)]
    [InlineData("method-redeclared-from-a-constructed-base", "MIST0177", 1)]
    [InlineData("method-redeclared-from-a-grandparent", "MIST0177", 1)]
    [InlineData("method-redeclared-over-a-refinement-of-the-inherited-parameter", "MIST0177", 1)]
    [InlineData("method-redeclared-through-a-two-level-generic-base", "MIST0177", 1)]
    [InlineData("method-redeclared-with-renamed-type-parameters", "MIST0177", 1)]
    [InlineData("newtype-over-sealed-type", "MIST0164", 1)]
    [InlineData("operator-not-on-declaring-type", "MIST0172", 1)]
    [InlineData("operator-not-public", "MIST0169", 1)]
    [InlineData("operator-unpaired-equality", "MIST0170", 1)]
    [InlineData("operator-unpaired-ordering", "MIST0170", 2)]
    [InlineData("property-redeclared-from-a-base", "MIST0177", 1)]
    [InlineData("sealed-base-type", "MIST0162", 1)]
    [InlineData("sealed-type-alias", "MIST0165", 1)]
    public void RefusesNamedCase(string name, string expected, int count)
    {
        var fragment = Build(name);
        var analyzer = name == "default-of-non-defaultable"
            ? (Mist.ProgramAnalysis.IProgramAnalyzer)new DefaultabilityAnalyzer()
            : new DeclarationShapeAnalyzer();
        var findings = fragment.Run(analyzer);
        Assert.Equal(count, findings.Count);
        Assert.All(findings, finding => Assert.Equal(expected, finding.Number.Value));
        Assert.All(findings, finding => Assert.Equal("hand-built.mist", finding.Location.Path));
    }

    [Theory]
    [InlineData("method")]
    [InlineData("constructor")]
    [InlineData("indexer")]
    public void DuplicateReportsTheLaterDeclaration(string kind)
    {
        var fragment = Duplicate($"duplicate-{kind}-signature");
        var finding = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));
        Assert.Equal(31, finding.Position().Start.Column);
    }

    [Fact]
    public void DistinctOverloadMayShareAnInheritedName()
    {
        var fragment = new DeclarationFragments();
        var parent = fragment.Class("Base");
        var derived = fragment.Class("Derived");
        derived.BaseType = parent;
        _ = DeclarationFragments.Method(parent, "Take", fragment.Built.Type("Int32"), fragment.Built.Type("Int32"));
        _ = DeclarationFragments.Method(derived, "Take", fragment.Built.Type("Int32"), fragment.Built.Type("String"));
        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
    }

    private static DeclarationFragments Build(string name)
    {
        if(name.StartsWith("duplicate-", StringComparison.Ordinal))
        {
            return Duplicate(name);
        }

        if(name.Contains("redeclared", StringComparison.Ordinal))
        {
            return Redeclaration(name);
        }

        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        integer.Modifiers = DeclarationFragments.Public with { IsReadonly = true };
        var owner = fragment.Class("Holder");

        switch(name)
        {
            case "default-entry-not-a-field":
                owner.FieldsList.Add(new Field("a", integer, OwnershipKind.Unrestricted, DeclarationFragments.Public));
                owner.DefaultBlock = new DefaultBlock([new DefaultValueEntry("b", HandBuiltModel.Literal(integer, 1L, 30))],
                    DeclarationFragments.At<DefaultBlockDeclarationNode>());
                break;
            case "default-of-non-defaultable":
                owner.ConstructorsList.Add(new Constructor("Holder", owner, [new Parameter("x", integer)], DeclarationFragments.Public));
                fragment.Root = new DefaultExpression(HandBuiltModel.Node(30), owner);
                break;
            case "sealed-base-type":
            case "newtype-over-sealed-type":
                owner.Modifiers = owner.Modifiers with { IsSealed = true };

                if(name == "sealed-base-type")
                {
                    fragment.Class("Derived").BaseType = owner;
                }
                else
                {
                    fragment.Types.Add(new Newtype("Wrapped", "Refusal", null, null,
                        DeclarationFragments.At<NewtypeDeclarationNode>())
                    { SourceType = owner });
                }

                break;
            case "sealed-type-alias":
                fragment.Alias("Alias", integer).Modifiers = DeclarationFragments.Public with { IsSealed = true };
                break;
            case "enum-base-not-readonly":
            case "enum-union-constituent-not-readonly":
            case "enum-duplicate-value":
                var underlying = name == "enum-duplicate-value" ? integer
                    : name == "enum-base-not-readonly" ? owner
                    : fragment.Built.Interner.GetOrCreateUnionType([integer, owner]);
                var enumeration = new EnumType("Bad", "Refusal", null, null, DeclarationFragments.At<EnumDeclarationNode>())
                {
                    UnderlyingType = underlying,
                    Modifiers = DeclarationFragments.Public,
                };
                fragment.Types.Add(enumeration);

                if(name == "enum-duplicate-value")
                {
                    enumeration.MembersList.Add(new EnumMember("A", false, HandBuiltModel.Literal(integer, 1L),
                        DeclarationFragments.At<EnumMemberNode>(20)));
                    enumeration.MembersList.Add(new EnumMember("B", false, HandBuiltModel.Literal(integer, 1L),
                        DeclarationFragments.At<EnumMemberNode>(30)));
                }

                break;
            default:
                var kind = name switch
                {
                    "logical-operator-declared" => OperatorKind.LogicalAnd,
                    "operator-unpaired-equality" => OperatorKind.EqualEqual,
                    "operator-unpaired-ordering" => OperatorKind.LessThan,
                    _ => OperatorKind.Plus,
                };
                var operand = name == "operator-not-on-declaring-type" ? (IMistType)integer : owner;
                owner.OperatorsList.Add(new Operator(kind, kind is OperatorKind.Plus ? owner : fragment.Built.Type("Boolean"),
                    [new Parameter("a", operand), new Parameter("b", operand)], DeclarationFragments.Public with
                    {
                        IsStatic = true,
                        Accessibility = name == "operator-not-public" ? Accessibility.Private : Accessibility.Public,
                    })
                { DeclarationNode = HandBuiltModel.Node(30) });

                if(name == "operator-unpaired-ordering")
                {
                    owner.OperatorsList.Add(new Operator(OperatorKind.GreaterThan, fragment.Built.Type("Boolean"),
                        [new Parameter("a", owner), new Parameter("b", integer)], DeclarationFragments.Public)
                    {
                        DeclarationNode = HandBuiltModel.Node(40),
                    });
                }

                break;
        }

        return fragment;
    }

    private static DeclarationFragments Duplicate(string name)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var text = fragment.Built.Type("String");
        MemberType owner = name == "duplicate-abstract-method-signature" ? fragment.Abstract("IShape") : fragment.Class("Sink");
        IMistType first = integer;
        IMistType second = integer;

        if(name == "duplicate-method-differing-only-in-tuple-element-names")
        {
            var tuple = fragment.Built.Interner.GetOrCreateTupleType([integer, integer]);
            first = new NamedTupleType(tuple, ["x", "y"]);
            second = new NamedTupleType(tuple, ["a", "b"]);
        }

        if(name == "duplicate-method-differing-only-in-params")
        {
            first = second = fragment.Built.Interner.GetOrCreateConstructedType(fragment.Built.Type("Array<,>"),
                [integer, fragment.Built.Interner.GetOrCreateUnknownValue(integer)]);
        }

        if(name == "duplicate-method-over-a-refinement-of-the-other-parameter")
        {
            second = HandBuiltModel.Refined(integer, "Positive");
        }

        if(name == "duplicate-method-over-two-spellings-of-an-optional")
        {
            first = fragment.Built.Interner.GetOrCreateUnionType([integer, fragment.Built.Type("Void")]);
            second = fragment.Built.Interner.GetOrCreateUnionType([fragment.Built.Type("Void"), integer]);
        }

        if(name == "duplicate-method-differing-only-in-ownership")
        {
            first = second = fragment.Class("Holder");
        }

        var byref = name == "duplicate-method-differing-only-in-readonly-ref";
        var leftParameter = new Parameter("first", first, new ParameterModifier(false, byref, OwnershipKind.Unrestricted));
        var rightParameter = new Parameter("second", second, new ParameterModifier(byref, byref,
            name == "duplicate-method-differing-only-in-ownership" ? OwnershipKind.Owned : OwnershipKind.Unrestricted))
        {
            IsFlattened = name == "duplicate-method-differing-only-in-params",
        };

        if(name == "duplicate-constructor-signature")
        {
            var concrete = (ConcreteType)owner;
            concrete.ConstructorsList.Add(new Constructor("Sink", owner, [leftParameter], DeclarationFragments.Public)
            {
                DeclarationNode = HandBuiltModel.Node(20),
            });
            concrete.ConstructorsList.Add(new Constructor("Sink", owner, [rightParameter], DeclarationFragments.Public)
            {
                DeclarationNode = HandBuiltModel.Node(30),
            });
            return fragment;
        }

        if(name == "duplicate-indexer-signature")
        {
            owner.IndexersList.Add(new Indexer(integer, [leftParameter], DeclarationFragments.Public)
            {
                DeclarationNode = HandBuiltModel.Node(20),
            });
            owner.IndexersList.Add(new Indexer(text, [rightParameter], DeclarationFragments.Public)
            {
                DeclarationNode = HandBuiltModel.Node(30),
            });
            return fragment;
        }

        var left = new Method("Take", integer, [leftParameter], DeclarationFragments.Public with { IsStatic = true })
        {
            DeclarationNode = HandBuiltModel.Node(20),
        };
        var right = new Method("Take", name == "duplicate-method-differing-only-in-return-type" ? text : integer,
            [rightParameter], DeclarationFragments.Public with { IsStatic = name != "duplicate-method-static-against-instance" })
        {
            DeclarationNode = HandBuiltModel.Node(30),
        };
        owner.MethodsList.Add(left);
        owner.MethodsList.Add(right);

        if(name.StartsWith("duplicate-generic-method-", StringComparison.Ordinal))
        {
            var a = DeclarationFragments.Parameter(left, owner.Scope, "A");
            var z = DeclarationFragments.Parameter(right, owner.Scope, "Z");
            leftParameter.Type = a;
            rightParameter.Type = z;

            if(name.Contains("union", StringComparison.Ordinal))
            {
                var other = fragment.Class("M");
                leftParameter.Type = fragment.Built.Interner.GetOrCreateUnionType([a, other]);
                rightParameter.Type = fragment.Built.Interner.GetOrCreateUnionType([z, other]);
            }
        }

        if(name == "duplicate-method-declared-three-times")
        {
            owner.MethodsList.Add(new Method("Take", integer, [new Parameter("third", integer)], DeclarationFragments.Public)
            {
                DeclarationNode = HandBuiltModel.Node(40),
            });
        }

        return fragment;
    }

    private static DeclarationFragments Redeclaration(string name)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var parent = fragment.Class("Base");
        var derived = fragment.Class("Derived");
        derived.BaseType = parent;
        IMistType inherited = integer;
        IMistType later = integer;

        if(name.Contains("generic-base", StringComparison.Ordinal) || name == "method-redeclared-from-a-constructed-base")
        {
            var parameter = DeclarationFragments.Parameter(parent, parent.Scope, "T");
            inherited = name == "method-redeclared-from-a-constructed-base" ? integer : parameter;
            derived.BaseType = fragment.Built.Interner.GetOrCreateConstructedType(parent, [integer]);

            if(name == "method-redeclared-through-a-two-level-generic-base")
            {
                var middle = fragment.Class("Middle");
                var other = DeclarationFragments.Parameter(middle, middle.Scope, "U");
                middle.BaseType = fragment.Built.Interner.GetOrCreateConstructedType(parent, [other]);
                derived.BaseType = fragment.Built.Interner.GetOrCreateConstructedType(middle, [integer]);
            }
        }

        if(name == "method-redeclared-from-a-grandparent")
        {
            var middle = fragment.Class("Middle");
            middle.BaseType = parent;
            derived.BaseType = middle;
            _ = DeclarationFragments.Method(middle, "Give", integer, integer);
        }

        if(name == "method-redeclared-over-a-refinement-of-the-inherited-parameter")
        {
            later = HandBuiltModel.Refined(integer, "Positive");
        }

        if(name.StartsWith("field-", StringComparison.Ordinal))
        {
            if(name == "field-redeclared-over-an-inherited-method")
            {
                _ = DeclarationFragments.Method(parent, "Size", integer);
            }
            else
            {
                parent.FieldsList.Add(new Field("Size", integer, OwnershipKind.Unrestricted, DeclarationFragments.Public)
                {
                    DeclarationNode = HandBuiltModel.Node(20),
                });
            }

            derived.FieldsList.Add(new Field("Size", integer, OwnershipKind.Unrestricted, DeclarationFragments.Public)
            {
                DeclarationNode = HandBuiltModel.Node(30),
            });
        }
        else if(name.StartsWith("property-", StringComparison.Ordinal))
        {
            parent.PropertiesList.Add(new Property("Size", integer, DeclarationFragments.Public) { DeclarationNode = HandBuiltModel.Node(20) });
            derived.PropertiesList.Add(new Property("Size", integer, DeclarationFragments.Public) { DeclarationNode = HandBuiltModel.Node(30) });
        }
        else if(name.StartsWith("indexer-", StringComparison.Ordinal))
        {
            parent.IndexersList.Add(new Indexer(integer, [new Parameter("first", inherited)], DeclarationFragments.Public)
            {
                DeclarationNode = HandBuiltModel.Node(20),
            });
            derived.IndexersList.Add(new Indexer(integer, [new Parameter("second", later)], DeclarationFragments.Public)
            {
                DeclarationNode = HandBuiltModel.Node(30),
            });
        }
        else
        {
            var first = DeclarationFragments.Method(parent, "Take", integer, inherited);
            var second = DeclarationFragments.Method(derived, "Take", integer, later);

            if(name == "method-redeclared-with-renamed-type-parameters")
            {
                first.ParametersList[0].Type = DeclarationFragments.Parameter(first, parent.Scope, "T");
                second.ParametersList[0].Type = DeclarationFragments.Parameter(second, derived.Scope, "U");
            }
        }

        return fragment;
    }
}