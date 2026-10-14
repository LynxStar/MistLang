using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

// A slice through a user type's Slice is written as receiver[range], which Roslyn lowers onto the same count
// and Slice the binder recorded. These are the places that writing needs what the model alone does not say.
[Trait("Tier", "2")]
public sealed class CountedSliceEmissionTests
{
    private const string Unsupported = "MIST_BE_UNSUPPORTED_IMPLICIT_SLICE";

    private const int PostfixPrecedence = 10;

    [Fact]
    public void ASliceReadThroughSliceIsTheRangeAccessRoslynLowers()
    {
        var f = new Fixture(Accessibility.Public);
        var diagnostics = new DiagnosticBag();

        using(AsyncPromotion.Enter(f.Built.Model))
        {
            Assert.Equal("s[0..1]", BodyEmitter.Render(f.Sliced(), diagnostics));
        }

        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void APromotedSliceIsAwaitedAsAPromotedCallIs()
    {
        var f = new Fixture(Accessibility.Public);
        f.Built.Model.TaskDrops.RecordDrop(f.Slice);
        var diagnostics = new DiagnosticBag();

        using(AsyncPromotion.Enter(f.Built.Model))
        {
            Assert.Equal("await s[0..1]", BodyEmitter.Render(f.Sliced(), diagnostics));
            Assert.Equal("(await s[0..1])", BodyEmitter.RenderAt(f.Sliced(), PostfixPrecedence, diagnostics));
            Assert.Equal("await s[0..1]", BodyEmitter.RenderAt(f.Sliced(), PostfixPrecedence - 1, diagnostics));
            Assert.True(AsyncLifts.NeedsAwait(f.Sliced()));
        }

        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AListPatternCannotWaitForAPromotedSlice()
    {
        var f = new Fixture(Accessibility.Public);
        f.Built.Model.TaskDrops.RecordDrop(f.Slice);
        var diagnostics = new DiagnosticBag();

        using(AsyncPromotion.Enter(f.Built.Model))
        {
            _ = BodyEmitter.Render(f.Matched(), diagnostics);
        }

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(Unsupported, diagnostic.Id.Value);
        Assert.Contains("'Slice'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Accessibility.Private, true)]
    [InlineData(Accessibility.Public, false)]
    public void ACloneConversionCannotSliceThroughAMemberCSharpRefusesItThere(Accessibility access, bool refused)
    {
        var f = new Fixture(access);
        var clone = HandBuiltModel.Class("Copy");
        var conversion = HandBuiltModel.Conversion(clone, f.Strip, clone);
        var diagnostics = new DiagnosticBag();

        using(AsyncPromotion.Enter(f.Built.Model))
        using(UnsafeReach.Enter(clone, conversion, "__Out", diagnostics))
        {
            _ = BodyEmitter.Render(f.Sliced(), diagnostics);
            _ = BodyEmitter.Render(f.Matched(), diagnostics);
        }

        Assert.Equal(refused ? [Unsupported, Unsupported] : [], diagnostics.ToImmutableArray().Select(diagnostic => diagnostic.Id.Value));
    }

    private sealed class Fixture
    {
        private readonly CountedSlice read;
        private readonly LocalVariable s;

        public Fixture(Accessibility sliceAccess)
        {
            var integer = Built.Type("Int32");
            Strip = HandBuiltModel.Class("Strip");
            var count = new Property("Count", integer, new MemberModifiers { Accessibility = Accessibility.Public })
            {
                DeclaringType = Strip,
                Getter = new PropertyAccessor { IsAuto = true },
            };
            Slice = new Method("Slice", integer, [new Parameter("start", integer), new Parameter("length", integer)], new MemberModifiers { Accessibility = sliceAccess })
            {
                DeclaringType = Strip,
            };
            Strip.PropertiesList.Add(count);
            Strip.MethodsList.Add(Slice);
            read = new CountedSlice(count, null, Slice);
            s = new LocalVariable("s", Strip);
            var task = new ClassType("Task<>", "Standard", new Scope(null), null, null);
            task.TypeParametersList.Add(new TypeParameter("T", 0, task.Scope, task));
            _ = Built.Model.RegisterType(task);
        }

        public HandBuiltModel Built { get; } = new();

        public ClassType Strip { get; }

        public Method Slice { get; }

        public IndexExpression Sliced()
        {
            var integer = Built.Type("Int32");
            var range = new RangeExpression(HandBuiltModel.Node(), HandBuiltModel.Literal(integer, 0), HandBuiltModel.Literal(integer, 1), Built.Type("Range"));
            return new IndexExpression(HandBuiltModel.Node(), new LocalReferenceExpression(HandBuiltModel.Node(), s), range, integer) { Slice = read };
        }

        public IsPatternExpression Matched()
        {
            var integer = Built.Type("Int32");
            var middle = new ListPatternElement(new CatchAllPattern(HandBuiltModel.Node(), integer, null), IsSlice: true, integer);
            var pattern = new ListPattern(HandBuiltModel.Node(), [middle], Strip) { Slice = read };
            return new IsPatternExpression(HandBuiltModel.Node(), new LocalReferenceExpression(HandBuiltModel.Node(), s), pattern, Built.Type("Boolean"));
        }
    }
}