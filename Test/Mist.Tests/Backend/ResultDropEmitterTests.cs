// How a Result's owned payload is released when the Result itself goes out of scope: whether a
// release is owed, which spelling each Result shape takes, and the Drop() an inspection snapshot's
// carrier declares. Extracting or discarding a Result preserves its active payload's cleanup
// obligation (specs/exceptions design, "Extracting or discarding a Result"); releasing that payload
// is the compiler honouring the obligation, not silently ignoring a user's fallible cleanup.

using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class ResultDropEmitterTests
{
    private readonly LifetimeFragments f = new();
    private readonly ResultModel shapes = new();

    /// <summary>
    /// A payload owes a release when it is linear or affine with a Drop. A relevant or unrestricted
    /// payload does not, whichever Result shape holds it: the Result adds no obligation of its own.
    /// </summary>
    [Theory]
    [Trait("Tier", "1")]
    [InlineData("result-success")]
    [InlineData("result-error")]
    [InlineData("projection")]
    [InlineData("inspection")]
    public void OnlyALinearOrAffinePayloadMakesAResultOweARelease(string shape)
    {
        Assert.Equal(
            [true, true, false, false],
            new IMistType[] { f.Token, f.Once, f.Shared, f.Plain }.Select(payload => ResultDropEmitter.Needed(Holding(shape, payload))));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void OneOwedPayloadAmongUnowedOnesIsEnough()
    {
        Assert.True(ResultDropEmitter.Needed(shapes.Result(f.Shared, f.Token)));
        Assert.True(ResultDropEmitter.Needed(Inspection(f.Plain, f.Once)));
        Assert.False(ResultDropEmitter.Needed(shapes.Result(f.Shared, f.Plain)));
    }

    /// <summary>
    /// A droppable value that is not a Result releases through its own Drop, which the structural
    /// release must leave to the ordinary drop path.
    /// </summary>
    [Fact]
    [Trait("Tier", "1")]
    public void ADroppableValueThatIsNotAResultOwesNoStructuralRelease()
    {
        Assert.False(ResultDropEmitter.Needed(f.Token));
        Assert.Null(ResultDropEmitter.Release("t", f.Token));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AResultReleasesItsActivePayloadThroughItsInspection()
    {
        Assert.Equal("(r).Inspect().Drop()", ResultDropEmitter.Release("r", shapes.Result(f.Token, shapes.Text)));
        Assert.Equal("(r).Inspect().Drop()", ResultDropEmitter.Release("r", shapes.Result(shapes.Integer, f.Once)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AProjectionReleasesItsPayloadByThatPayloadsOwnRelease()
    {
        Assert.Equal("(r).Payload.Drop()", ResultDropEmitter.Release("r", shapes.Projection(f.Token, success: true)));
        Assert.Equal("(r).Payload.Drop()", ResultDropEmitter.Release("r", shapes.Projection(f.Once, success: false)));
        Assert.Equal(
            "((r).Payload).Inspect().Drop()",
            ResultDropEmitter.Release("r", shapes.Projection(shapes.Result(f.Token, shapes.Text), success: true)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AnInspectionSnapshotReleasesThroughItsCarriersDrop()
    {
        Assert.Equal("(r).Drop()", ResultDropEmitter.Release("r", Inspection(f.Token, shapes.Text)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void NothingIsReleasedWhenNoPayloadIsOwed()
    {
        Assert.Null(ResultDropEmitter.Release("r", shapes.Result(shapes.Integer, shapes.Text)));
        Assert.Null(ResultDropEmitter.Release("r", shapes.Result(f.Shared, f.Plain)));
        Assert.Null(ResultDropEmitter.Release("r", shapes.Projection(f.Shared, success: true)));
        Assert.Null(ResultDropEmitter.Release("r", Inspection(shapes.Integer, f.Plain)));
    }

    /// <summary>
    /// Inside a specialization a Result over a type parameter is judged by the argument it is closed
    /// over, not by the open parameter.
    /// </summary>
    [Fact]
    [Trait("Tier", "1")]
    public void AnOpenResultIsJudgedByTheArgumentItIsClosedOver()
    {
        var owner = HandBuiltModel.Class("Owner");
        var parameter = new TypeParameter("T", 0, owner.Scope, owner);
        owner.TypeParametersList.Add(parameter);
        var open = shapes.Result(parameter, shapes.Text);
        var projected = shapes.Projection(parameter, success: true);

        using(TypeNameEmitter.Substituting(TypeSubstitution.ForParameters([parameter], [f.Token])))
        {
            Assert.True(ResultDropEmitter.Needed(open));
            Assert.Equal("(r).Inspect().Drop()", ResultDropEmitter.Release("r", open));
            Assert.Equal("(r).Payload.Drop()", ResultDropEmitter.Release("r", projected));
        }

        using(TypeNameEmitter.Substituting(TypeSubstitution.ForParameters([parameter], [shapes.Integer])))
        {
            Assert.False(ResultDropEmitter.Needed(open));
            Assert.Null(ResultDropEmitter.Release("r", open));
            Assert.Null(ResultDropEmitter.Release("r", projected));
        }
    }

    /// <summary>
    /// The carrier stores constituent N under tag N, counting from one, so the Drop reads the payload
    /// slot of exactly the constituents that owe a release, at their position rather than their case.
    /// </summary>
    [Fact]
    [Trait("Tier", "1")]
    public void ACarriersDropReleasesOnlyTheOwedConstituentUnderItsPositionsTag()
    {
        Assert.Equal(
            """

            public void Drop()
            {
                if (_tag == 2)
                {
                    (((bool, Once))_value).Item2.Drop();
                }
            }

            """,
            Emitted(Ordered((shapes.True, shapes.Integer), (shapes.False, f.Once))));
        Assert.Equal(
            """

            public void Drop()
            {
                if (_tag == 1)
                {
                    (((bool, Once))_value).Item2.Drop();
                }
            }

            """,
            Emitted(Ordered((shapes.False, f.Once), (shapes.True, shapes.Integer))));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ACarriersDropReleasesEveryOwedConstituentUnderItsOwnTag()
    {
        Assert.Equal(
            """

            public void Drop()
            {
                if (_tag == 1)
                {
                    (((bool, Token))_value).Item2.Drop();
                }
                if (_tag == 2)
                {
                    (((bool, Once))_value).Item2.Drop();
                }
            }

            """,
            Emitted(Ordered((shapes.True, f.Token), (shapes.False, f.Once))));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void ACarriersDropReleasesANestedResultThroughItsInspection()
    {
        Assert.Contains(
            "((((bool, Standard.Result<Token, string>))_value).Item2).Inspect().Drop();",
            Emitted(Inspection(shapes.Result(f.Token, shapes.Text), shapes.Text)));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void NoDropIsDeclaredWhenNothingIsOwedOrTheUnionIsNoInspection()
    {
        var interner = shapes.Built.Interner;
        var untagged = new UnionType([
            interner.GetOrCreateTupleType([shapes.Integer, f.Token]), interner.GetOrCreateTupleType([shapes.Text, f.Token])]);

        Assert.Equal("", Emitted(Inspection(shapes.Integer, f.Plain)));
        Assert.Equal("", Emitted(untagged));
    }

    private IMistType Holding(string shape, IMistType payload)
    {
        return shape switch
        {
            "result-success" => shapes.Result(payload, shapes.Text),
            "result-error" => shapes.Result(shapes.Integer, payload),
            "projection" => shapes.Projection(payload, success: true),
            _ => Inspection(payload, shapes.Text),
        };
    }

    private UnionType Inspection(IMistType success, IMistType error)
    {
        return shapes.Inspection(success, error);
    }

    private UnionType Ordered(params (IMistType Tag, IMistType Payload)[] constituents)
    {
        return new UnionType([.. constituents.Select(constituent => shapes.Built.Interner.GetOrCreateTupleType([constituent.Tag, constituent.Payload]))]);
    }

    private static string Emitted(UnionType union)
    {
        var writer = new CSharpWriter();
        ResultDropEmitter.Emit(union, writer);
        return writer.ToString();
    }
}