using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// A range into a countable user type selects its Range indexer, or failing one its Slice(int, int), and a list
// pattern's slice does the same. Asserted on the model: the emitted receiver[range] is lowered by Roslyn, so a
// program that runs cannot say which member Mist recorded for the rules that judge the call.
[Trait("Tier", "2")]
public sealed class UserSliceBindingTests
{
    private const string NotSliceable = "MIST_PM_RECEIVER_NOT_SLICEABLE";

    [Fact]
    public void ARangeIndexerOfACountedReceiverIsTheSelectedAccess()
    {
        var f = new Fixture();
        var spans = f.Type("Spans");
        _ = DeclareCount(spans, "Length", f.Int32);
        _ = DeclareIndexer(spans, f.Int32, f.Int32);
        var ranged = DeclareIndexer(spans, f.Range, f.String);

        var sliced = Assert.IsType<IndexExpression>(f.Slice(spans));

        Assert.Empty(f.Errors());
        Assert.Same(ranged, sliced.Indexer);
        Assert.Same(f.String, sliced.Type);
        Assert.Same(f.Range, sliced.ParameterType);
        Assert.Null(sliced.Slice);
    }

    [Fact]
    public void AnInheritedGenericRangeIndexerIsReadThroughItsConstructedBase()
    {
        var f = new Fixture();
        var labelled = f.Type("Labelled");
        var label = new TypeParameter("T", 0, labelled.Scope, labelled);
        labelled.TypeParametersList.Add(label);
        _ = DeclareCount(labelled, "Count", f.Int32);
        var ranged = DeclareIndexer(labelled, f.Range, label);
        var named = f.Type("Named");
        named.BaseType = f.Built.Interner.GetOrCreateConstructedType(labelled, [f.String]);

        var sliced = Assert.IsType<IndexExpression>(f.Slice(named));

        Assert.Empty(f.Errors());
        Assert.Same(ranged, sliced.Indexer);
        Assert.Same(f.String, sliced.Type);
    }

    [Fact]
    public void SliceIsReachedWhenNoIndexerTakesTheRange()
    {
        var f = new Fixture();
        var cuts = f.Type("Cuts");
        var count = DeclareCount(cuts, "Count", f.Int32);
        _ = DeclareIndexer(cuts, f.Int32, f.Int32);
        var slice = DeclareSlice(cuts, f.String, f.Int32, f.Int32);

        var sliced = Assert.IsType<IndexExpression>(f.Slice(cuts));

        Assert.Empty(f.Errors());
        Assert.Null(sliced.Indexer);
        Assert.Null(sliced.ParameterType);
        Assert.Same(f.String, sliced.Type);
        Assert.Equal(new CountedSlice(count, null, slice), sliced.Slice);
    }

    [Fact]
    public void AGenericOwnersSliceResultIsClosedByTheReceiver()
    {
        var f = new Fixture();
        var seq = f.Type("Seq");
        var element = new TypeParameter("T", 0, seq.Scope, seq);
        seq.TypeParametersList.Add(element);
        _ = DeclareCount(seq, "Length", f.Int32);
        _ = DeclareSlice(seq, f.Built.Interner.GetOrCreateConstructedType(seq, [element]), f.Int32, f.Int32);
        var ofText = f.Built.Interner.GetOrCreateConstructedType(seq, [f.String]);

        var sliced = Assert.IsType<IndexExpression>(f.Slice(ofText));

        Assert.Empty(f.Errors());
        Assert.Same(ofText, sliced.Type);
    }

    [Fact]
    public void LengthIsCountedBeforeCount_AndARangeIndexerBeforeSlice()
    {
        var f = new Fixture();
        var both = f.Type("Both");
        var length = DeclareCount(both, "Length", f.Int32);
        _ = DeclareCount(both, "Count", f.Int32);
        _ = DeclareSlice(both, f.Int32, f.Int32, f.Int32);
        var ranged = DeclareIndexer(both, f.Range, f.String);

        var sliced = Assert.IsType<IndexExpression>(f.Slice(both));
        var counted = Indexability.CountedSliceOf(both, HandBuiltModel.Node(), f.Fragments.Builder);

        Assert.Same(ranged, sliced.Indexer);
        Assert.Equal((f.String, new CountedSlice(length, ranged, null)), counted);
    }

    [Theory]
    [InlineData("range-indexer-without-a-count")]
    [InlineData("slice-without-a-count")]
    [InlineData("long-count")]
    [InlineData("count-without-a-getter")]
    [InlineData("slice-of-longs")]
    [InlineData("slice-of-one")]
    [InlineData("slice-of-three")]
    [InlineData("static-slice")]
    [InlineData("generic-slice")]
    [InlineData("ref-slice")]
    [InlineData("int-indexer-alone")]
    public void AReceiverOutsideBothProtocolsIsRefusedOnce_AndItsFailureIsSpoken(string shape)
    {
        var f = new Fixture();
        var receiver = f.Type("Shape");
        Declare(f, receiver, shape);

        var refused = f.Slice(receiver);

        var diagnostic = Assert.Single(f.Errors());
        Assert.Equal(NotSliceable, diagnostic.Id.Value);
        Assert.StartsWith("'Shape' cannot be sliced by a range.", diagnostic.Message, StringComparison.Ordinal);
        Assert.True(PriorFailure.Of(refused));
    }

    [Fact]
    public void AListPatternSliceRecordsTheSelectedSlice_AndBindsItsResult()
    {
        var f = new Fixture();
        var cuts = f.Type("Cuts");
        var count = DeclareCount(cuts, "Count", f.Int32);
        _ = DeclareIndexer(cuts, f.Int32, f.Int32);
        var slice = DeclareSlice(cuts, f.String, f.Int32, f.Int32);

        var bound = f.Match(cuts, Patterns.Element(Patterns.Var("first")), Patterns.Slice(Patterns.Var("middle")), Patterns.Element(Patterns.Var("last")));

        Assert.Empty(f.Errors());
        Assert.Equal(new CountedSlice(count, null, slice), bound.Slice);
        Assert.NotNull(bound.Reads);
        Assert.Same(f.String, bound.Elements[1].ScrutineeType);
        Assert.Same(f.String, f.Fragments.Scope.Locals["middle"].Type);
        Assert.Same(f.Int32, f.Fragments.Scope.Locals["first"].Type);
    }

    [Fact]
    public void AListPatternSliceReadsARangeIndexerAgainstTheCount()
    {
        var f = new Fixture();
        var spans = f.Type("Spans");
        var length = DeclareCount(spans, "Length", f.Int32);
        _ = DeclareIndexer(spans, f.Int32, f.Int32);
        var ranged = DeclareIndexer(spans, f.Range, f.String);

        var bound = f.Match(spans, Patterns.Element(Patterns.Var("first")), Patterns.Slice(Patterns.Var("rest")));

        Assert.Empty(f.Errors());
        Assert.Equal(new CountedSlice(length, ranged, null), bound.Slice);
        Assert.Same(f.String, bound.Elements[1].ScrutineeType);
    }

    [Fact]
    public void ABareSliceNeedsNoSliceProtocol()
    {
        var f = new Fixture();
        var rows = f.Type("Rows");
        _ = DeclareCount(rows, "Count", f.Int32);
        _ = DeclareIndexer(rows, f.Int32, f.Int32);

        var bound = f.Match(rows, Patterns.Element(Patterns.Var("first")), Patterns.Slice(null));

        Assert.Empty(f.Errors());
        Assert.Null(bound.Slice);
        Assert.False(bound.IsFailed);
    }

    [Theory]
    [InlineData("unsliceable", NotSliceable)]
    [InlineData("two-slices", "MIST_PM_LIST_PATTERN_MULTIPLE_SLICES")]
    [InlineData("unindexable", "MIST_PM_LIST_PATTERN_SCRUTINEE")]
    public void ARefusedListPatternKeepsItsBindingsAsFailures_SoNothingNamesThemAgain(string refusal, string id)
    {
        var f = new Fixture();
        var rows = f.Type("Rows");

        if(refusal != "unindexable")
        {
            _ = DeclareCount(rows, "Count", f.Int32);
            _ = DeclareIndexer(rows, f.Int32, f.Int32);
        }

        if(refusal == "two-slices")
        {
            _ = DeclareSlice(rows, f.String, f.Int32, f.Int32);
        }

        var bound = refusal == "two-slices"
            ? f.Match(rows, Patterns.Slice(Patterns.Var("first")), Patterns.Slice(Patterns.Var("middle")), Patterns.Element(Patterns.Var("last")))
            : f.Match(rows, Patterns.Element(Patterns.Var("first")), Patterns.Slice(Patterns.Var("middle")), Patterns.Element(Patterns.Var("last")));

        Assert.Equal(id, Assert.Single(f.Errors()).Id.Value);
        Assert.True(bound.IsFailed);
        Assert.Equal(3, bound.Elements.Count);
        Assert.Equal(refusal == "unindexable", f.Fragments.Scope.Locals["first"].IsFailed);
        Assert.All(new[] { "middle", "last" }, name => Assert.True(f.Fragments.Scope.Locals[name].IsFailed, name));
    }

    private static void Declare(Fixture f, ClassType receiver, string shape)
    {
        var counted = shape is not ("range-indexer-without-a-count" or "slice-without-a-count" or "long-count" or "count-without-a-getter");

        if(counted)
        {
            _ = DeclareCount(receiver, "Count", f.Int32);
        }

        switch(shape)
        {
            case "range-indexer-without-a-count":
                _ = DeclareIndexer(receiver, f.Range, f.Int32);
                break;
            case "slice-without-a-count":
                _ = DeclareSlice(receiver, f.Int32, f.Int32, f.Int32);
                break;
            case "long-count":
                _ = DeclareCount(receiver, "Length", f.Built.Type("Int64"));
                _ = DeclareSlice(receiver, f.Int32, f.Int32, f.Int32);
                break;
            case "count-without-a-getter":
                receiver.PropertiesList.Add(new Property("Count", f.Int32, DeclarationFragments.Public)
                {
                    DeclaringType = receiver,
                    Setter = new PropertyAccessor { IsAuto = true },
                });
                _ = DeclareSlice(receiver, f.Int32, f.Int32, f.Int32);
                break;
            case "slice-of-longs":
                _ = DeclareSlice(receiver, f.Int32, f.Built.Type("Int64"), f.Built.Type("Int64"));
                break;
            case "slice-of-one":
                _ = DeclareSlice(receiver, f.Int32, f.Int32);
                break;
            case "slice-of-three":
                _ = DeclareSlice(receiver, f.Int32, f.Int32, f.Int32, f.Int32);
                break;
            case "static-slice":
                receiver.MethodsList.Add(new Method(
                    "Slice", f.Int32, [new Parameter("start", f.Int32), new Parameter("length", f.Int32)], DeclarationFragments.Public with { IsStatic = true })
                {
                    DeclaringType = receiver,
                    Scope = receiver.Scope,
                });
                break;
            case "generic-slice":
                _ = BindingFragments.Parameter(DeclareSlice(receiver, f.Int32, f.Int32, f.Int32), "T");
                break;
            case "ref-slice":
                var slice = DeclareSlice(receiver, f.Int32, f.Int32, f.Int32);
                slice.ParametersList[1] = new Parameter("length", f.Int32, new ParameterModifier(false, true, OwnershipKind.Unrestricted, ByRefDirection.Ref));
                break;
            case "int-indexer-alone":
                _ = DeclareIndexer(receiver, f.Int32, f.Int32);
                break;
        }
    }

    private static Property DeclareCount(MemberType owner, string name, IMistType type)
    {
        var property = new Property(name, type, DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor { IsAuto = true },
        };
        owner.PropertiesList.Add(property);
        return property;
    }

    private static Indexer DeclareIndexer(MemberType owner, IMistType parameter, IMistType result)
    {
        var indexer = new Indexer(result, [new Parameter("at", parameter)], DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor { IsAuto = true },
        };
        owner.IndexersList.Add(indexer);
        return indexer;
    }

    private static Method DeclareSlice(MemberType owner, IMistType result, params IMistType[] parameters)
    {
        return BindingFragments.Member(owner, "Slice", result, parameters);
    }

    private sealed class Fixture
    {
        public BindingFragments Fragments { get; } = new();

        public HandBuiltModel Built => Fragments.Built;

        public ClassType Int32 => Built.Type("Int32");

        public ClassType String => Built.Type("String");

        public ClassType Range => Built.Type("Range");

        public Fixture()
        {
            Fragments.Register(Range);
            _ = Fragments.Local("window", Range);
        }

        public ClassType Type(string name)
        {
            var type = HandBuiltModel.Class(name);
            Fragments.Register(type);
            return type;
        }

        public Expression Slice(IMistType receiver)
        {
            _ = Fragments.Local("receiver", receiver);
            var index = BindingFragments.At<IndexSuffixPostfixSuffixNode>();
            index.Index = BindingFragments.Expression(BindingFragments.Identifier("window"));
            return Fragments.Bind(BindingFragments.Identifier("receiver"), index);
        }

        public ListPattern Match(IMistType scrutinee, params ListPatternElementNode[] elements)
        {
            return Assert.IsType<ListPattern>(PatternBinder.Bind(Patterns.List(elements), scrutinee, Fragments.Binding, Fragments.Builder));
        }

        public IEnumerable<Mist.Common.Diagnostics.Diagnostic> Errors()
        {
            return Fragments.Diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error);
        }
    }

    private static class Patterns
    {
        public static PatternNode List(params ListPatternElementNode[] elements)
        {
            var list = BindingFragments.At<ListPatternPrimaryPatternNode>();
            list.Elements = new ListPatternElementListNode { ListPatternElement = elements[0], ListPatternElements = [.. elements.Skip(1)] };
            return Pattern(list);
        }

        public static ListPatternElementNode Element(PatternNode pattern)
        {
            return new PatternElementListPatternElementNode { Pattern = pattern };
        }

        public static ListPatternElementNode Slice(PatternNode? subpattern)
        {
            return new SliceElementListPatternElementNode { Subpattern = subpattern };
        }

        public static PatternNode Var(string name)
        {
            var primary = BindingFragments.At<VarPatternPrimaryPatternNode>();
            primary.Name = BindingFragments.Token(name);
            return Pattern(primary);
        }

        private static PatternNode Pattern(PrimaryPatternNode primary)
        {
            return new()
            {
                OrPattern = new OrPatternNode { Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = primary } } },
            };
        }
    }
}