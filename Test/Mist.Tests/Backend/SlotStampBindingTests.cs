using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class SlotStampBindingTests : ProgramModelProbe
{
    public SlotStampBindingTests()
        : base("MistSlotStampBinding_")
    {
    }

    protected override string Fixtures { get; } = """
        public struct P
        {
            public int Y = 7;
        }

        public struct Box<T> where T : stampable
        {
            public T Item;
        }

        public abstract IMaker
        {
            T[2] Make<T>() where T : stampable;
            Box<T>[2] Boxes<T>() where T : stampable;
        }

        public class Maker : IMaker
        {
            public T[2] Make<T>() where T : stampable
            {
                return new T[2];
            }

            public Box<T>[2] Boxes<T>() where T : stampable
            {
                return new Box<T>[2];
            }
        }

        public class Caller
        {
            public static int Run(IMaker maker)
            {
                return maker.Make<P>()[0].Y + maker.Boxes<P>()[0].Item.Y;
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [Theory]
    [InlineData("Make", "Probe.P")]
    [InlineData("Boxes", "Probe.Box<Probe.P>")]
    public void AMethodAnsweringASlot_IsBoundToWhatTheSlotIsCalledWith(string method, string expected)
    {
        var model = Build($"using Standard;\nnamespace Probe;\n{Fixtures}", out var diagnostics);

        Assert.Empty(diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));

        var maker = Assert.IsType<ClassType>(model.TypesByFQN["Probe.Maker"]);
        var answering = maker.MethodsList.Single(candidate => candidate.Name == method);
        var element = BodyWalker.DescendantsAndSelf(answering.Body!).OfType<ArrayCreationExpression>().Single().ElementType;

        var closed = StampInstantiations.Close(model, [element]).Select(TypeNameEmitter.Emit);

        Assert.Contains(expected, closed);
    }
}