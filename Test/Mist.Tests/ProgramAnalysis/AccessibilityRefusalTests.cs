using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

// Tier 1 for name decisions; Tier 2 for accessibility walks over direct model fragments.
public sealed class AccessibilityRefusalTests
{
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("private-generic-and-its-private-nested-type-are-refused-once")]
    [InlineData("private-nested-alias-as-a-foreach-type")]
    [InlineData("private-nested-alias-as-a-local-type")]
    [InlineData("private-nested-alias-as-a-return-type-in-another-type")]
    [InlineData("private-nested-alias-in-a-cast")]
    [InlineData("private-nested-alias-in-a-function-type")]
    [InlineData("private-nested-alias-in-a-tuple-type")]
    [InlineData("private-nested-alias-named-by-a-top-level-alias")]
    [InlineData("private-nested-alias-refined-by-a-top-level-alias")]
    [InlineData("private-nested-attribute-applied-outside-its-type")]
    [InlineData("private-nested-enum-variant-in-a-pattern")]
    [InlineData("private-nested-optional-in-a-public-field-is-refused-once")]
    [InlineData("private-nested-type-as-a-field-type-in-another-type")]
    [InlineData("private-nested-type-as-a-generic-constraint")]
    [InlineData("private-nested-type-as-a-generic-method-type-argument")]
    [InlineData("private-nested-type-as-a-lambda-parameter")]
    [InlineData("private-nested-type-as-a-local-function-parameter")]
    [InlineData("private-nested-type-as-a-local-type")]
    [InlineData("private-nested-type-as-a-namespace-qualified-receiver")]
    [InlineData("private-nested-type-as-a-newtype-source")]
    [InlineData("private-nested-type-as-a-parameter-type-in-another-type")]
    [InlineData("private-nested-type-as-a-property-type-in-another-type")]
    [InlineData("private-nested-type-as-a-static-receiver")]
    [InlineData("private-nested-type-as-a-type-argument")]
    [InlineData("private-nested-type-as-a-value")]
    [InlineData("private-nested-type-as-an-array-creation-element-type")]
    [InlineData("private-nested-type-as-an-array-element-type")]
    [InlineData("private-nested-type-as-an-extension-receiver")]
    [InlineData("private-nested-type-beside-a-clone-conversion")]
    [InlineData("private-nested-type-in-a-public-signature-is-refused-once")]
    [InlineData("private-nested-type-in-a-typed-property-pattern")]
    [InlineData("private-nested-type-in-a-union")]
    [InlineData("private-nested-type-in-as")]
    [InlineData("private-nested-type-in-default")]
    [InlineData("private-nested-type-in-is")]
    [InlineData("private-nested-type-in-new")]
    [InlineData("private-nested-type-refined-over-its-private-field-is-refused-once")]
    [InlineData("private-nested-type-with-a-private-constructor-is-refused-once")]
    [InlineData("private-nested-type-with-a-private-field-in-a-pattern-is-refused-once")]
    [InlineData("private-nested-type-with-a-private-method-is-refused-once")]
    [InlineData("private-nested-type-with-a-private-setter-is-refused-once")]
    [InlineData("private-nested-variadic-type-in-new")]
    [InlineData("private-refinement-alias-named-outside-its-type")]
    [InlineData("private-type-nested-in-a-generic-instantiation")]
    [InlineData("protected-nested-type-named-outside-a-derivation")]
    [InlineData("public-type-nested-in-a-private-type-named-outside")]
    public void PrivateTypeNamingIsRefused(string name)
    {
        var fragment = Naming(name);
        var refusal = AccessibilityAnalyzer.DecideNaming(fragment.Namings[0]);
        _ = Assert.NotNull(refusal);
        Assert.Equal("MIST0160", refusal.Value.Descriptor.Number.Value);
        Assert.Same(name is "private-generic-and-its-private-nested-type-are-refused-once" or "public-type-nested-in-a-private-type-named-outside"
            ? fragment.Namings[0].Named.DeclaringType : fragment.Namings[0].Named, refusal.Value.Blocking);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("private-field-in-a-property-pattern")]
    [InlineData("private-field-in-an-initializer")]
    [InlineData("private-indexer-read-outside-its-type")]
    [InlineData("private-member-access")]
    [InlineData("private-method-call")]
    [InlineData("private-property-in-a-property-pattern")]
    [InlineData("private-property-in-an-initializer")]
    [InlineData("protected-member-access")]
    [InlineData("protected-method-call")]
    [InlineData("protected-property-with-a-private-setter-written-outside")]
    [InlineData("refinement-alias-reads-a-member-out-of-its-reach")]
    [InlineData("refinement-bound-names-a-member-out-of-its-reach")]
    [InlineData("refinement-extension-where-reads-a-member-out-of-its-reach")]
    [InlineData("refinement-local-function-where-reads-a-member-out-of-its-reach")]
    [InlineData("refinement-nested-subpattern-reads-a-member-out-of-its-reach")]
    public void MemberUseIsRefused(string name)
    {
        var fragment = name.StartsWith("refinement-", StringComparison.Ordinal) ? PredicateAccess(name) : MemberUse(name);
        var diagnostic = Assert.Single(fragment.Run(new AccessibilityAnalyzer()));
        Assert.Equal("MIST0159", diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
        Assert.Equal(31, diagnostic.Position().Start.Column);

        if(name == "protected-property-with-a-private-setter-written-outside")
        {
            Assert.Empty(fragment.Run(new AccessorAnalyzer()));
        }
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("base-type-less-accessible")]
    [InlineData("constructed-type-argument-less-accessible")]
    [InlineData("field-type-less-accessible")]
    [InlineData("nested-in-internal-type-less-accessible")]
    [InlineData("nested-type-less-accessible")]
    [InlineData("parameter-type-less-accessible")]
    [InlineData("property-type-less-accessible")]
    [InlineData("refinement-alias-more-accessible-than-the-member-it-reads")]
    [InlineData("refinement-parameter-more-accessible-than-the-member-it-reads")]
    [InlineData("return-type-less-accessible")]
    public void ExposureIsRefused(string name)
    {
        var fragment = name.StartsWith("refinement-", StringComparison.Ordinal) ? PredicateAccess(name) : Exposure(name);
        var diagnostic = Assert.Single(fragment.Run(new AccessibilityAnalyzer()));
        Assert.Equal("MIST0161", diagnostic.Number.Value);
        Assert.Equal("hand-built.mist", diagnostic.Location.Path);
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData("private-generic-and-its-private-nested-type-are-refused-once")]
    [InlineData("private-nested-optional-in-a-public-field-is-refused-once")]
    [InlineData("private-nested-type-in-a-public-signature-is-refused-once")]
    [InlineData("private-nested-type-refined-over-its-private-field-is-refused-once")]
    [InlineData("private-nested-type-with-a-private-constructor-is-refused-once")]
    [InlineData("private-nested-type-with-a-private-field-in-a-pattern-is-refused-once")]
    [InlineData("private-nested-type-with-a-private-method-is-refused-once")]
    [InlineData("private-nested-type-with-a-private-setter-is-refused-once")]
    public void InaccessibleTypeSuppressesDerivedFindings(string name)
    {
        var fragment = Naming(name);
        var finding = Assert.Single(fragment.Run(new AccessibilityAnalyzer()));
        Assert.Equal("MIST0160", finding.Number.Value);
        Assert.Equal(31, finding.Position().Start.Column);
        Assert.Empty(fragment.Run(new AccessorAnalyzer()));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ProtectedNameIsAdmittedInsideADerivation()
    {
        var fragment = Naming("protected-nested-type-named-outside-a-derivation");
        var naming = fragment.Namings[0];
        var derived = fragment.Class("Derived");
        derived.BaseType = naming.Named.DeclaringType;
        Assert.Null(AccessibilityAnalyzer.DecideNaming(naming with { Owner = derived }));
    }

    private static DeclarationFragments Naming(string name)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var outer = fragment.Class("Gauge");
        var probe = fragment.Class("Probe");
        fragment.Owner = probe;
        DeclaredType named;

        if(name.Contains("alias", StringComparison.Ordinal))
        {
            named = fragment.Alias("Count", name.Contains("refinement", StringComparison.Ordinal)
                ? HandBuiltModel.Refined(outer, "Low") : integer, outer, Accessibility.Private);
        }
        else if(name == "public-type-nested-in-a-private-type-named-outside")
        {
            var hidden = fragment.Class("Outer", outer, Accessibility.Private);
            named = fragment.Class("Inner", hidden);
        }
        else if(name == "private-nested-enum-variant-in-a-pattern")
        {
            named = new EnumType("Mode", "Refusal", null, outer, DeclarationFragments.At<EnumDeclarationNode>())
            {
                Modifiers = DeclarationFragments.Public with { Accessibility = Accessibility.Private },
            };
        }
        else
        {
            if(name == "private-generic-and-its-private-nested-type-are-refused-once")
            {
                outer = fragment.Class("Box", outer, Accessibility.Private);
                _ = DeclarationFragments.Parameter(outer, outer.Scope, "T");
            }
            else if(name == "private-type-nested-in-a-generic-instantiation")
            {
                _ = DeclarationFragments.Parameter(outer, outer.Scope, "T");
            }

            named = fragment.Class(name == "private-nested-attribute-applied-outside-its-type" ? "MarkAttribute" : "Hidden", outer,
                name == "protected-nested-type-named-outside-a-derivation" ? Accessibility.Protected : Accessibility.Private);

            if(name == "private-nested-variadic-type-in-new" && named is MemberType variadic)
            {
                var pack = DeclarationFragments.Parameter(variadic, variadic.Scope, "Ts");
                pack.PackKind = TypePackKind.Unbracketed;
            }
        }

        var at = HandBuiltModel.Node(30);
        fragment.Namings.Add(new TypeNaming(named, at, probe, MistAssembly.Synthetic));

        if(name == "private-generic-and-its-private-nested-type-are-refused-once")
        {
            fragment.Namings.Add(new TypeNaming(outer, at, probe, MistAssembly.Synthetic));
        }

        if(name.Contains("public-signature", StringComparison.Ordinal))
        {
            _ = DeclarationFragments.Method(probe, "Read", integer, named);
        }

        if(name.Contains("public-field", StringComparison.Ordinal))
        {
            probe.FieldsList.Add(new Field("kept", fragment.Built.Interner.GetOrCreateUnionType([named, fragment.Built.Type("Void")]),
                OwnershipKind.Unrestricted, DeclarationFragments.Public)
            { DeclarationNode = HandBuiltModel.Node(30) });
        }

        if(named is ClassType concrete)
        {
            var secret = new Field("secret", integer, OwnershipKind.Unrestricted,
                DeclarationFragments.Public with { Accessibility = Accessibility.Private, IsReadonly = true })
            { DeclaringType = concrete };
            concrete.FieldsList.Add(secret);

            if(name.Contains("private-constructor", StringComparison.Ordinal))
            {
                var constructor = new Constructor("Hidden", concrete, [], DeclarationFragments.Public with { Accessibility = Accessibility.Private })
                {
                    DeclaringType = concrete,
                };
                concrete.ConstructorsList.Add(constructor);
                fragment.Root = new NewExpression(HandBuiltModel.Node(30), concrete, [], constructor, [], []);
            }
            else if(name.Contains("private-field-in-a-pattern", StringComparison.Ordinal))
            {
                fragment.Root = PropertyPattern(concrete, integer, new FieldResult(secret, concrete, []));
            }
            else if(name.Contains("refined-over-its-private-field", StringComparison.Ordinal))
            {
                var refined = HandBuiltModel.Refined(concrete, "Low");
                refined.Predicate = PropertyPattern(concrete, integer, new FieldResult(secret, concrete, []));
                _ = fragment.Alias("Low", refined);
            }
            else if(name.Contains("private-method", StringComparison.Ordinal))
            {
                var method = new Method("Make", integer, [], DeclarationFragments.Public with { Accessibility = Accessibility.Private })
                {
                    DeclaringType = concrete,
                };
                concrete.MethodsList.Add(method);
                fragment.Root = new InvocationExpression(HandBuiltModel.Node(30), new ThisExpression(HandBuiltModel.Node(), concrete),
                    [], integer, method, []);
            }
            else if(name.Contains("private-setter", StringComparison.Ordinal))
            {
                var property = new Property("Prop", integer, DeclarationFragments.Public)
                {
                    DeclaringType = concrete,
                    Getter = new PropertyAccessor { IsAuto = true },
                    Setter = new PropertyAccessor { IsAuto = true, Accessibility = Accessibility.Private },
                };
                concrete.PropertiesList.Add(property);
                var access = new MemberAccessExpression(HandBuiltModel.Node(30), new ThisExpression(HandBuiltModel.Node(), concrete),
                    "Prop", integer, member: new PropertyResult(property, concrete, []));
                fragment.Root = new AssignmentExpression(HandBuiltModel.Node(), access, HandBuiltModel.Literal(integer, 3L), integer);
            }
        }

        return fragment;
    }

    private static DeclarationFragments MemberUse(string name)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var owner = fragment.Class("Owner");
        fragment.Owner = fragment.Class("Probe");
        var access = name.StartsWith("protected-", StringComparison.Ordinal) ? Accessibility.Protected : Accessibility.Private;
        var modifiers = DeclarationFragments.Public with { Accessibility = access };
        var receiver = new ThisExpression(HandBuiltModel.Node(), owner);

        if(name.Contains("method-call", StringComparison.Ordinal))
        {
            var method = new Method("Hidden", integer, [], modifiers) { DeclaringType = owner };
            fragment.Root = new InvocationExpression(HandBuiltModel.Node(30), receiver, [], integer, method, []);
            return fragment;
        }

        if(name.Contains("indexer", StringComparison.Ordinal))
        {
            var indexer = new Indexer(integer, [new Parameter("slot", integer)], modifiers)
            {
                DeclaringType = owner,
                Getter = new PropertyAccessor { IsAuto = true },
            };
            fragment.Root = new IndexExpression(HandBuiltModel.Node(30), receiver, HandBuiltModel.Literal(integer, 3L), integer)
            {
                Indexer = indexer,
            };
            return fragment;
        }

        LookupResult member;

        if(name.Contains("property", StringComparison.Ordinal))
        {
            var property = new Property("Secret", integer, modifiers)
            {
                DeclaringType = owner,
                Getter = new PropertyAccessor { IsAuto = true },
                Setter = new PropertyAccessor
                {
                    IsAuto = true,
                    Accessibility = name.Contains("private-setter", StringComparison.Ordinal) ? Accessibility.Private : Accessibility.Public,
                },
            };
            member = new PropertyResult(property, owner, []);
        }
        else
        {
            var field = new Field("secret", integer, OwnershipKind.Unrestricted, modifiers) { DeclaringType = owner };
            member = new FieldResult(field, owner, []);
        }

        if(name.Contains("property-pattern", StringComparison.Ordinal))
        {
            fragment.Root = PropertyPattern(owner, integer, member);
        }
        else if(name.Contains("initializer", StringComparison.Ordinal))
        {
            fragment.Root = new ObjectInitializerAssignment(HandBuiltModel.Node(30), "secret", HandBuiltModel.Literal(integer, 5L),
                integer, member: member);
        }
        else
        {
            var use = new MemberAccessExpression(HandBuiltModel.Node(30), receiver, "secret", integer, member: member);
            fragment.Root = name.Contains("written", StringComparison.Ordinal)
                ? new AssignmentExpression(HandBuiltModel.Node(), use, HandBuiltModel.Literal(integer, 3L), integer)
                : use;
        }

        return fragment;
    }

    private static DeclarationFragments Exposure(string name)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var owner = fragment.Class("Exposes");
        IMistType hidden;

        if(name is "nested-type-less-accessible" or "constructed-type-argument-less-accessible")
        {
            hidden = fragment.Class("Hidden", owner, Accessibility.Private);

            if(name == "constructed-type-argument-less-accessible")
            {
                hidden = fragment.Built.Interner.GetOrCreateConstructedType(fragment.Built.Type("List<>"), [hidden]);
            }
        }
        else
        {
            hidden = name == "nested-in-internal-type-less-accessible"
                ? fragment.Class("Inner", fragment.Class("Outer", access: Accessibility.Internal))
                : fragment.Class("Unmarked", access: Accessibility.Internal);
        }

        switch(name)
        {
            case "base-type-less-accessible":
                owner.BaseType = (MistType)hidden;
                break;
            case "field-type-less-accessible":
                owner.FieldsList.Add(new Field("holder", hidden, OwnershipKind.Unrestricted, DeclarationFragments.Public)
                {
                    DeclarationNode = HandBuiltModel.Node(30),
                });
                break;
            case "property-type-less-accessible":
                owner.PropertiesList.Add(new Property("Prop", hidden, DeclarationFragments.Public)
                {
                    DeclarationNode = HandBuiltModel.Node(30),
                });
                break;
            case "parameter-type-less-accessible":
                _ = DeclarationFragments.Method(owner, "Take", integer, hidden);
                break;
            default:
                _ = DeclarationFragments.Method(owner, "Make", hidden);
                break;
        }

        return fragment;
    }

    private static DeclarationFragments PredicateAccess(string name)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var owner = fragment.Class("Gauge");
        var field = new Field("level", integer, OwnershipKind.Unrestricted,
            DeclarationFragments.Public with { Accessibility = Accessibility.Private, IsReadonly = true })
        { DeclaringType = owner };
        owner.FieldsList.Add(field);
        Pattern predicate = PropertyPattern(owner, integer, new FieldResult(field, owner, []));

        if(name == "refinement-bound-names-a-member-out-of-its-reach")
        {
            var bound = new MemberAccessExpression(HandBuiltModel.Node(30), new ThisExpression(HandBuiltModel.Node(), owner), "Max", integer,
                member: new FieldResult(field, owner, []));
            predicate = new ComparisonPattern(HandBuiltModel.Node(), owner, ComparisonKind.LessThan, bound);
        }

        if(name == "refinement-nested-subpattern-reads-a-member-out-of-its-reach")
        {
            var dial = fragment.Class("Dial");
            var notch = new Field("notch", integer, OwnershipKind.Unrestricted, field.Modifiers) { DeclaringType = dial };
            var inner = PropertyPattern(dial, integer, new FieldResult(notch, dial, []));
            var dialField = new Field("dial", dial, OwnershipKind.Unrestricted, DeclarationFragments.Public with { IsReadonly = true })
            {
                DeclaringType = owner,
            };
            predicate = new PropertyPattern(HandBuiltModel.Node(), owner,
                [new PropertySubpattern(HandBuiltModel.Node(), "dial", inner, dial, new FieldResult(dialField, owner, []))]);
        }

        var refined = HandBuiltModel.Refined(owner);
        refined.Predicate = predicate;

        if(name.Contains("parameter-more-accessible", StringComparison.Ordinal))
        {
            _ = DeclarationFragments.Method(owner, "Take", integer, refined);
        }
        else if(name.Contains("extension-where", StringComparison.Ordinal))
        {
            var block = new ExtensionBlock("self", "Refusal", new Scope(null), null) { ResolvedReceiverType = integer };
            block.MethodsList.Add(new Method("Take", integer, [new Parameter("gauge", refined)], DeclarationFragments.Public)
            {
                Body = HandBuiltModel.Block(),
            });
            fragment.Extensions.Add(block);
        }
        else if(name.Contains("local-function-where", StringComparison.Ordinal))
        {
            var method = new Method("Take", integer, [new Parameter("gauge", refined)], DeclarationFragments.Public)
            {
                Body = HandBuiltModel.Block(),
            };
            fragment.Owner = fragment.Class("Probe");
            fragment.Root = new LocalFunctionStatement(method, []);
        }
        else
        {
            _ = fragment.Alias("Low", refined, name.Contains("alias-more-accessible", StringComparison.Ordinal) ? owner : null);
        }

        return fragment;
    }

    private static PropertyPattern PropertyPattern(IMistType owner, IMistType integer, LookupResult member)
    {
        var comparison = new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.LessThan, HandBuiltModel.Literal(integer, 3L));
        return new PropertyPattern(HandBuiltModel.Node(), owner,
            [new PropertySubpattern(HandBuiltModel.Node(30), "secret", comparison, integer, member)]);
    }
}