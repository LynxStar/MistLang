using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
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

// Tier 2: declared predicate discovery and stability walks over model inputs constructed directly.
[Trait("Tier", "2")]
public sealed class PredicateDeclarationRefusalTests
{
    [Theory]
    [InlineData("refinement-not-comparable-alias-bool", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-alias-generic", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-alias-union-constituent", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-alias-unknown", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-constructor-string", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-dependent-bool", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-dependent-method-bool", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-disjunction", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-extension-method-dependent-bool", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-extension-method", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-indexer-type-parameter", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-local-function", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-negated", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-optional", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-slot-bool", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-struct-with-operators", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-struct-without-operators", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-not-comparable-type-parameter", "MIST_PA_REFINEMENT_NOT_COMPARABLE")]
    [InlineData("refinement-over-a-clr-property", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-getter-computed-from-a-mutable-field", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-getter-matching-a-mutable-field", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-getter-matching-a-settable-property", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-getter-reading-a-settable-property", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-getter-that-assigns", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-getter-that-calls-a-method", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-getter-that-indexes-an-array", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-getter-using-a-declared-conversion", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-getter-using-a-declared-operator", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-mutable-field", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-mutable-member-in-a-conjunction", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-mutable-member-in-a-disjunction", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-mutable-member-in-a-negation", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-mutable-member-in-a-union-constituent", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-nested-mutable-member", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-readonly-abstracts-property", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-a-settable-property", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-an-abstract-property", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-an-expression-bodied-property-reading-a-mutable-field", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("refinement-over-getters-that-read-each-other", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    [InlineData("where-pattern-over-a-mutable-member", "MIST_PA_REFINEMENT_OVER_MUTABLE_MEMBER")]
    public void RefusesNamedCase(string name, string expected)
    {
        var fragment = name.StartsWith("refinement-not-comparable-", StringComparison.Ordinal) ? Misordered(name) : Unstable(name);
        var diagnostic = Assert.Single(fragment.Run(new DeclarationShapeAnalyzer()));
        Assert.Equal(expected, diagnostic.Id.Value);
        Assert.Equal("hand-built.mist", diagnostic.CompilationUnitPath);
        Assert.Equal(31, diagnostic.Location.Start.Column);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StableStoragePredicateIsSilent(bool field)
    {
        var fragment = new DeclarationFragments();
        var owner = fragment.Class("Jar");
        var integer = fragment.Built.Type("Int32");
        LookupResult member = field
            ? new FieldResult(new Field("Size", integer, OwnershipKind.Unrestricted,
                DeclarationFragments.Public with { IsReadonly = true })
            { DeclaringType = owner }, owner, [])
            : new PropertyResult(new Property("Size", integer, DeclarationFragments.Public)
            {
                DeclaringType = owner,
                Getter = new PropertyAccessor { IsAuto = true },
            }, owner, []);
        Alias(fragment, owner, Property(owner, integer, member));
        Assert.Empty(fragment.Run(new DeclarationShapeAnalyzer()));
    }

    private static DeclarationFragments Misordered(string name)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        var boolean = fragment.Built.Type("Boolean");
        var text = fragment.Built.Type("String");
        var owner = name == "refinement-not-comparable-slot-bool" ? (MemberType)fragment.Abstract("Validator") : fragment.Class("Sink");
        var constrained = name.Contains("bool", StringComparison.Ordinal) || name.Contains("disjunction", StringComparison.Ordinal) ? boolean
            : name.Contains("string", StringComparison.Ordinal) || name.Contains("negated", StringComparison.Ordinal) ? text
            : name.Contains("unknown", StringComparison.Ordinal) ? UnknownType.Instance
            : name.Contains("optional", StringComparison.Ordinal) ? fragment.Built.Interner.GetOrCreateUnionType([integer, fragment.Built.Type("Void")])
            : name.Contains("generic", StringComparison.Ordinal) || name.Contains("type-parameter", StringComparison.Ordinal)
                ? DeclarationFragments.Parameter(owner, owner.Scope, "K") : fragment.Class("Jar");

        if(name.Contains("struct-", StringComparison.Ordinal))
        {
            var meters = new StructType("Meters", "Refusal", null, null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
            {
                Modifiers = DeclarationFragments.Public,
            };
            fragment.Types.Add(meters);
            constrained = meters;

            if(name.Contains("with-operators", StringComparison.Ordinal))
            {
                foreach(var kind in new[] { OperatorKind.LessThan, OperatorKind.GreaterThan, OperatorKind.LessThanOrEqual, OperatorKind.GreaterThanOrEqual })
                {
                    meters.OperatorsList.Add(new Operator(kind, boolean, [new Parameter("a", meters), new Parameter("b", meters)],
                        DeclarationFragments.Public)
                    { DeclarationNode = HandBuiltModel.Node() });
                }
            }
        }

        Pattern predicate = new ComparisonPattern(HandBuiltModel.Node(30), constrained, ComparisonKind.GreaterThanOrEqual,
            HandBuiltModel.Literal(name.Contains("bool", StringComparison.Ordinal) ? boolean : integer,
                name.Contains("bool", StringComparison.Ordinal) ? true : 18L));

        if(name == "refinement-not-comparable-disjunction")
        {
            predicate = new DisjunctionPattern(HandBuiltModel.Node(), constrained,
                [predicate, new ComparisonPattern(HandBuiltModel.Node(40), constrained, ComparisonKind.LessThan, HandBuiltModel.Literal(integer, 0L))]);
        }
        else if(name == "refinement-not-comparable-negated")
        {
            predicate = new NegatedPattern(HandBuiltModel.Node(), constrained, predicate);
        }
        else if(name == "refinement-not-comparable-struct-without-operators")
        {
            predicate = new ConjunctionPattern(HandBuiltModel.Node(), constrained,
                [predicate, new ComparisonPattern(HandBuiltModel.Node(40), constrained, ComparisonKind.LessThanOrEqual, HandBuiltModel.Literal(integer, 30L))]);
        }

        if(name == "refinement-not-comparable-alias-union-constituent")
        {
            var good = HandBuiltModel.Refined(integer, "Count");
            good.Predicate = new ComparisonPattern(HandBuiltModel.Node(), integer, ComparisonKind.GreaterThanOrEqual, HandBuiltModel.Literal(integer, 0L));
            var bad = HandBuiltModel.Refined(text, "Text");
            bad.Predicate = new ComparisonPattern(HandBuiltModel.Node(30), text, ComparisonKind.GreaterThan, HandBuiltModel.Literal(text, "a"));
            _ = fragment.Alias("Mixed", fragment.Built.Interner.GetOrCreateUnionType([good, bad]));
        }
        else
        {
            Attach(fragment, owner, constrained, predicate, name);
        }

        return fragment;
    }

    private static void Attach(DeclarationFragments fragment, MemberType owner, IMistType constrained, Pattern predicate, string name)
    {
        if(name.Contains("alias-", StringComparison.Ordinal) || name.StartsWith("refinement-over-", StringComparison.Ordinal))
        {
            Alias(fragment, constrained, predicate, name.Contains("union-constituent", StringComparison.Ordinal));
            return;
        }

        var refined = HandBuiltModel.Refined(constrained);
        refined.Predicate = predicate;
        var integer = fragment.Built.Type("Int32");

        if(name.Contains("constructor", StringComparison.Ordinal))
        {
            ((ConcreteType)owner).ConstructorsList.Add(new Constructor("Sink", owner, [new Parameter("s", refined)], DeclarationFragments.Public));
        }
        else if(name.Contains("indexer", StringComparison.Ordinal))
        {
            owner.IndexersList.Add(new Indexer(integer, [new Parameter("key", refined)], DeclarationFragments.Public));
        }
        else if(name.Contains("dependent-bool", StringComparison.Ordinal) && !name.Contains("method", StringComparison.Ordinal))
        {
            var parameter = DeclarationFragments.Parameter(owner, owner.Scope, "Flag");
            parameter.Domain = (MistType)constrained;
            parameter.IsDependent = true;
            parameter.Refinement = predicate;
        }
        else
        {
            var method = new Method("Take", integer, name.Contains("dependent", StringComparison.Ordinal) ? [] : [new Parameter("value", refined)],
                DeclarationFragments.Public)
            { Body = HandBuiltModel.Block() };

            if(name.Contains("dependent", StringComparison.Ordinal))
            {
                var parameter = DeclarationFragments.Parameter(method, owner.Scope, "Flag");
                parameter.Domain = (MistType)constrained;
                parameter.IsDependent = true;
                parameter.Refinement = predicate;
            }

            if(name.Contains("extension", StringComparison.Ordinal))
            {
                var block = new ExtensionBlock("self", "Refusal", new Scope(null), null) { ResolvedReceiverType = constrained };
                block.MethodsList.Add(method);
                fragment.Extensions.Add(block);
                fragment.Root = method.Body;
            }
            else if(name.Contains("local-function", StringComparison.Ordinal))
            {
                fragment.Root = new LocalFunctionStatement(method, []);
                fragment.Owner = owner;
            }
            else
            {
                owner.MethodsList.Add(method);
            }
        }
    }

    private static void Alias(DeclarationFragments fragment, IMistType constrained, Pattern predicate, bool union = false)
    {
        var refined = HandBuiltModel.Refined(constrained, "Low");
        refined.Predicate = predicate;
        _ = fragment.Alias("Low", union ? fragment.Built.Interner.GetOrCreateUnionType([refined, fragment.Built.Type("Int32")]) : refined);
    }

    private static DeclarationFragments Unstable(string name)
    {
        var fragment = new DeclarationFragments();
        var integer = fragment.Built.Type("Int32");
        MemberType owner = name is "refinement-over-an-abstract-property" or "refinement-over-a-readonly-abstracts-property"
            ? fragment.Abstract("ISized") : fragment.Class("Jar");

        if(name == "refinement-over-a-readonly-abstracts-property")
        {
            owner.Modifiers = owner.Modifiers with { IsReadonly = true };
        }

        if(name == "refinement-over-a-clr-property")
        {
            owner.Modifiers = owner.Modifiers with { IsClrProjected = true };
        }

        var field = new Field("size", integer, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = owner };
        var getter = new PropertyAccessor { IsAuto = owner is ConcreteType && name != "refinement-over-a-clr-property" };
        PropertyAccessor? setter = new() { IsAuto = true };
        IMistType memberType = integer;

        if(name.Contains("getter", StringComparison.Ordinal) || name.Contains("expression-bodied", StringComparison.Ordinal))
        {
            getter = Getter(fragment, owner, field, name, out memberType);
            setter = null;
        }
        else if(owner is AbstractType || name == "refinement-over-a-clr-property")
        {
            setter = null;
        }

        var property = new Property("Size", memberType, DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Getter = getter,
            Setter = setter,
        };
        owner.PropertiesList.Add(property);
        LookupResult unstable = name == "refinement-over-a-mutable-field" ? new FieldResult(field, owner, []) : new PropertyResult(property, owner, []);
        Pattern predicate = Property(owner, memberType, unstable);

        if(name == "refinement-over-a-getter-using-a-declared-operator")
        {
            var worth = new Field("Worth", integer, OwnershipKind.Unrestricted, DeclarationFragments.Public with { IsReadonly = true });
            var nested = Property(memberType, integer, new FieldResult(worth, memberType, []), 40);
            predicate = new PropertyPattern(HandBuiltModel.Node(), owner,
                [new PropertySubpattern(HandBuiltModel.Node(30), "Both", nested, memberType, unstable)]);
        }
        else if(name == "refinement-over-a-nested-mutable-member")
        {
            var lid = fragment.Class("Lid");
            var width = new Property("Width", integer, DeclarationFragments.Public)
            {
                DeclaringType = lid,
                Getter = new PropertyAccessor { IsAuto = true },
                Setter = new PropertyAccessor { IsAuto = true },
            };
            var top = new Property("Top", lid, DeclarationFragments.Public)
            {
                DeclaringType = owner,
                Getter = new PropertyAccessor { IsAuto = true },
            };
            predicate = new PropertyPattern(HandBuiltModel.Node(), owner,
                [new PropertySubpattern(HandBuiltModel.Node(), "Top", Property(lid, integer, new PropertyResult(width, lid, [])), lid,
                    new PropertyResult(top, owner, []))]);
        }
        else if(name is "refinement-over-a-mutable-member-in-a-conjunction" or "refinement-over-a-mutable-member-in-a-disjunction")
        {
            var stable = new Property("Stable", integer, DeclarationFragments.Public)
            {
                DeclaringType = owner,
                Getter = new PropertyAccessor { IsAuto = true },
            };
            var first = Property(owner, integer, new PropertyResult(stable, owner, []), 10);
            predicate = name.EndsWith("conjunction", StringComparison.Ordinal)
                ? new ConjunctionPattern(HandBuiltModel.Node(), owner, [first, predicate])
                : new DisjunctionPattern(HandBuiltModel.Node(), owner, [first, predicate]);
        }
        else if(name == "refinement-over-a-mutable-member-in-a-negation")
        {
            predicate = new NegatedPattern(HandBuiltModel.Node(), owner, predicate);
        }

        Attach(fragment, owner, owner, predicate, name);
        return fragment;
    }

    private static PropertyAccessor Getter(DeclarationFragments fragment, MemberType owner, Field mutable, string name, out IMistType returns)
    {
        var integer = fragment.Built.Type("Int32");
        returns = integer;
        Expression value = new FieldReferenceExpression(HandBuiltModel.Node(), mutable);

        if(name == "refinement-over-getters-that-read-each-other")
        {
            var leftGetter = new PropertyAccessor();
            var rightGetter = new PropertyAccessor();
            var left = new Property("Size", integer, DeclarationFragments.Public) { DeclaringType = owner, Getter = leftGetter };
            var right = new Property("Other", integer, DeclarationFragments.Public) { DeclaringType = owner, Getter = rightGetter };
            leftGetter.ExpressionBody = new PropertyReferenceExpression(HandBuiltModel.Node(), right);
            rightGetter.ExpressionBody = new PropertyReferenceExpression(HandBuiltModel.Node(), left);
            owner.PropertiesList.Add(right);
            return leftGetter;
        }

        if(name == "refinement-over-a-getter-that-assigns")
        {
            var local = new LocalVariable("held", integer);
            return new PropertyAccessor
            {
                Body = HandBuiltModel.Block(new LocalDeclarationStatement(local, HandBuiltModel.Literal(integer, 0L)),
                    new ExpressionStatement(new AssignmentExpression(HandBuiltModel.Node(), new LocalReferenceExpression(HandBuiltModel.Node(), local),
                        value, integer)), new ReturnStatement(new LocalReferenceExpression(HandBuiltModel.Node(), local), integer)),
            };
        }

        if(name.Contains("matching", StringComparison.Ordinal))
        {
            LookupResult member = new FieldResult(mutable, owner, []);

            if(name.Contains("settable-property", StringComparison.Ordinal))
            {
                var weight = new Property("Weight", integer, DeclarationFragments.Public)
                {
                    DeclaringType = owner,
                    Getter = new PropertyAccessor { IsAuto = true },
                    Setter = new PropertyAccessor { IsAuto = true },
                };
                member = new PropertyResult(weight, owner, []);
            }

            var condition = new IsPatternExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), owner),
                Property(owner, integer, member), fragment.Built.Type("Boolean"));
            return new PropertyAccessor
            {
                Body = HandBuiltModel.Block(new IfStatement(condition, DeclarationFragments.Returning(HandBuiltModel.Literal(integer, 3L)), null),
                    new ReturnStatement(HandBuiltModel.Literal(integer, 1L), integer)),
            };
        }

        if(name.Contains("reading-a-settable-property", StringComparison.Ordinal))
        {
            var lid = fragment.Class("Lid");
            var width = new Property("Width", integer, DeclarationFragments.Public)
            {
                DeclaringType = lid,
                Getter = new PropertyAccessor { IsAuto = true },
                Setter = new PropertyAccessor { IsAuto = true },
            };
            var top = new Field("top", lid, OwnershipKind.Unrestricted, DeclarationFragments.Public with { IsReadonly = true });
            value = new MemberAccessExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), top), "Width", integer,
                member: new PropertyResult(width, lid, []));
        }
        else if(name.Contains("calls-a-method", StringComparison.Ordinal))
        {
            var measure = new Method("Measure", integer, [], DeclarationFragments.Public)
            {
                Body = DeclarationFragments.Returning(value),
            };
            value = new InvocationExpression(HandBuiltModel.Node(), new ThisExpression(HandBuiltModel.Node(), owner), [], integer, measure, []);
        }
        else if(name.Contains("indexes-an-array", StringComparison.Ordinal))
        {
            var array = fragment.Built.Interner.GetOrCreateConstructedType(fragment.Built.Type("Array<,>"),
                [integer, fragment.Built.Interner.LiftLiteral(integer, 3L)]);
            var cells = new Field("cells", array, OwnershipKind.Unrestricted, DeclarationFragments.Public with { IsReadonly = true });
            value = new IndexExpression(HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), cells),
                HandBuiltModel.Literal(integer, 0L), integer);
        }
        else if(name.Contains("declared-conversion", StringComparison.Ordinal) || name.Contains("declared-operator", StringComparison.Ordinal))
        {
            var coin = fragment.Class("Coin");
            var held = new Field("coin", coin, OwnershipKind.Unrestricted, DeclarationFragments.Public with { IsReadonly = true });
            var left = new FieldReferenceExpression(HandBuiltModel.Node(), held);

            if(name.Contains("declared-conversion", StringComparison.Ordinal))
            {
                var conversion = HandBuiltModel.Conversion(coin, coin, integer, isImplicit: false);
                value = new CastExpression(HandBuiltModel.Node(), left, integer, [new ConversionFunction(conversion, coin, integer)], integer);
            }
            else
            {
                returns = coin;
                var op = new Operator(OperatorKind.Plus, coin, [new Parameter("a", coin), new Parameter("b", coin)], DeclarationFragments.Public);
                value = new BinaryExpression(HandBuiltModel.Node(), left, BinaryOperatorKind.Add,
                    new FieldReferenceExpression(HandBuiltModel.Node(), held), coin)
                { Operator = op };
            }
        }

        return name.Contains("expression-bodied", StringComparison.Ordinal)
            ? new PropertyAccessor { ExpressionBody = value }
            : new PropertyAccessor { Body = DeclarationFragments.Returning(value) };
    }

    private static PropertyPattern Property(IMistType owner, IMistType memberType, LookupResult member, int offset = 30)
    {
        var narrowed = memberType;
        var comparison = new ComparisonPattern(HandBuiltModel.Node(), narrowed, ComparisonKind.LessThan,
            HandBuiltModel.Literal(memberType, 3L));
        return new PropertyPattern(HandBuiltModel.Node(), owner,
            [new PropertySubpattern(HandBuiltModel.Node(offset), "Size", comparison, memberType, member)]);
    }
}