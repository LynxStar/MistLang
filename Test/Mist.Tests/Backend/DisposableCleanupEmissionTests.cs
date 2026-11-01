using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed class DisposableCleanupEmissionTests
{
    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void DisposableFlagsReleaseTheEvaluatedValueOnceAfterPop(bool constructed)
    {
        var f = new LifetimeFragments();
        var resource = HandBuiltModel.Class(constructed ? "Resource<>" : "Resource");
        resource.HasDrop = true;
        resource.IsDisposableDrop = true;
        IMistType valueType = constructed ? new ConstructedType(resource, [f.Int32]) : resource;
        var make = new Method("Make", valueType, [], new MemberModifiers { IsStatic = true }) { DeclaringType = f.Sink };
        var local = new LocalVariable("stream", valueType);
        var scope = new WithBlockStatement(f.Call(make), HandBuiltModel.Block(), local, f.Plain);

        var emitted = LifetimeFragments.Emitted(HandBuiltModel.Block(scope));

        Assert.Equal(
            """
            {
                var stream = Sink.Make();
                MistContext.Push<Plain>(stream);
                try
                {
                }
                finally
                {
                    MistContext.Pop<Plain>();
                    stream.Dispose();
                }
            }
            """.ReplaceLineEndings("\n"), emitted);
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(SubstructuralDiscipline.None)]
    [InlineData(SubstructuralDiscipline.Linear)]
    [InlineData(SubstructuralDiscipline.Relevant)]
    public void AContainerDoesNotAcquireDropFromADisposableField(SubstructuralDiscipline discipline)
    {
        var resource = HandBuiltModel.Class("Resource");
        resource.HasDrop = true;
        resource.IsDisposableDrop = true;
        var container = HandBuiltModel.Class("Container");
        container.Modifiers = new MemberModifiers { Discipline = discipline };
        container.FieldsList.Add(new Field("Resource", resource, OwnershipKind.Unrestricted, default));

        Assert.False(DropInsertion.SynthesizesDrop(container));
    }

    [Theory]
    [Trait("Tier", "1")]
    [InlineData(false)]
    [InlineData(true)]
    public void SyntheticDropFieldsKeepExplicitDropsAndExcludeDisposableDrops(bool constructed)
    {
        var resource = HandBuiltModel.Class("Resource<>");
        resource.HasDrop = true;
        resource.IsDisposableDrop = true;
        IMistType valueType = constructed ? new ConstructedType(resource, [HandBuiltModel.Class("Argument")]) : resource;
        var explicitResource = HandBuiltModel.Class("ExplicitResource");
        explicitResource.MethodsList.Add(new Method("Drop", HandBuiltModel.Class("Result"), [], default));
        var container = HandBuiltModel.Class("Container");
        var retained = new Field("Explicit", explicitResource, OwnershipKind.Unrestricted, default);
        container.FieldsList.AddRange([new Field("Disposable", valueType, OwnershipKind.Unrestricted, default), retained]);

        Assert.Equal([retained], DropInsertion.SyntheticDropFields(container));
    }

    [Fact]
    [Trait("Tier", "1")]
    public void AnExplicitDropFlagKeepsDropEmission()
    {
        var resource = HandBuiltModel.Class("Resource");
        resource.HasDrop = true;
        resource.MethodsList.Add(new Method("Drop", HandBuiltModel.Class("Result"), [], default));

        Assert.Equal("Drop", DropInsertion.ExitAction(resource));
    }
}