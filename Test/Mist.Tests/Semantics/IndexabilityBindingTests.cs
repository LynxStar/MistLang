using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

// What an element access or a range reads is decided by the receiver's type: ranges-and-indices Requirements 8.3
// and 8.4, with the span slice's length from the stack-only-types Slice declaration. Asserted on the bound
// model, since the emitted receiver[arg] is lowered by Roslyn and a running program cannot say what Mist typed.
[Trait("Tier", "2")]
public sealed class IndexabilityBindingTests
{
    [Theory]
    [InlineData("Span<,>")]
    [InlineData("ReadOnlySpan<,>")]
    public void ARangeIntoASizedSpanKeepsItsKindAndElement_ButNotItsLength(string kind)
    {
        var f = new Fixture();
        var span = f.Built.Type(kind);
        f.Fragments.Register(span);
        var interner = f.Built.Interner;
        var sized = interner.GetOrCreateConstructedType(span, [f.String, new LiftedType(f.Int32, 5)]);

        var sliced = Assert.IsType<IndexExpression>(f.Access(sized, "window"));

        Assert.Empty(f.Errors());
        Assert.Same(interner.GetOrCreateConstructedType(span, [f.String, interner.GetOrCreateUnknownValue(f.Int32)]), sliced.Type);
    }

    [Fact]
    public void ACountOfARefinedIntStillCountsTheReceiver()
    {
        var f = new Fixture();
        var cuts = f.Type("Cuts");
        var count = DeclareCount(cuts, HandBuiltModel.Refined(f.Int32, "Natural"));
        var slice = BindingFragments.Member(cuts, "Slice", f.String, f.Int32, f.Int32);

        var sliced = Assert.IsType<IndexExpression>(f.Access(cuts, "window"));

        Assert.Empty(f.Errors());
        Assert.Same(f.String, sliced.Type);
        Assert.Equal(new CountedSlice(count, null, slice), sliced.Slice);
    }

    [Fact]
    public void ARefinedReceiverSlicesThroughItsBasesCountAndSlice()
    {
        var f = new Fixture();
        var cuts = f.Type("Cuts");
        var count = DeclareCount(cuts, f.Int32);
        var slice = BindingFragments.Member(cuts, "Slice", f.String, f.Int32, f.Int32);

        var sliced = Assert.IsType<IndexExpression>(f.Access(HandBuiltModel.Refined(cuts, "NonEmpty"), "window"));

        Assert.Empty(f.Errors());
        Assert.Same(f.String, sliced.Type);
        Assert.Equal(new CountedSlice(count, null, slice), sliced.Slice);
    }

    [Fact]
    public void AnElementReadThroughAConstructedConstraintIsClosedByThatConstraint()
    {
        var f = new Fixture();
        var labelled = f.Type("Labelled");
        var element = new TypeParameter("E", 0, labelled.Scope, labelled);
        labelled.TypeParametersList.Add(element);
        var indexer = new Indexer(element, [new Parameter("at", f.Int32)], DeclarationFragments.Public)
        {
            DeclaringType = labelled,
            Getter = new PropertyAccessor { IsAuto = true },
        };
        labelled.IndexersList.Add(indexer);
        var parameter = new TypeParameter("T", 0, f.Fragments.Scope, null);
        parameter.ConstraintsList.Add(new TypeConstraint(f.Built.Interner.GetOrCreateConstructedType(labelled, [f.String])));

        var read = Assert.IsType<IndexExpression>(f.Access(parameter, "position"));

        Assert.Empty(f.Errors());
        Assert.Same(indexer, read.Indexer);
        Assert.Same(f.String, read.Type);
    }

    private static Property DeclareCount(MemberType owner, IMistType type)
    {
        var property = new Property("Count", type, DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Getter = new PropertyAccessor { IsAuto = true },
        };
        owner.PropertiesList.Add(property);
        return property;
    }

    private sealed class Fixture
    {
        public BindingFragments Fragments { get; } = new();

        public HandBuiltModel Built => Fragments.Built;

        public ClassType Int32 => Built.Type("Int32");

        public ClassType String => Built.Type("String");

        public Fixture()
        {
            var range = Built.Type("Range");
            Fragments.Register(range);
            _ = Fragments.Local("window", range);
            _ = Fragments.Local("position", Int32);
        }

        public ClassType Type(string name)
        {
            var type = HandBuiltModel.Class(name);
            Fragments.Register(type);
            return type;
        }

        public Expression Access(IMistType receiver, string argument)
        {
            _ = Fragments.Local("receiver", receiver);
            var index = BindingFragments.At<IndexSuffixPostfixSuffixNode>();
            index.Index = BindingFragments.Expression(BindingFragments.Identifier(argument));
            return Fragments.Bind(BindingFragments.Identifier("receiver"), index);
        }

        public IEnumerable<Mist.Common.Diagnostics.Diagnostic> Errors()
        {
            return Fragments.Diagnostics.ToImmutableArray().Where(diagnostic => diagnostic.Severity == Mist.Common.Diagnostics.DiagnosticSeverity.Error);
        }
    }
}