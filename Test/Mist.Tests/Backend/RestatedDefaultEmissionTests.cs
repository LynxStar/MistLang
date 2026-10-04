// Tier 3 (Component): what a conformer emits for a GENERIC abstract's default. Asserted on the emitted
// text because the failure the sample would see is Roslyn's: a default restated with the abstract's T
// in a class where no T is in scope, in its body as much as its signature -- which is the defect
// classic ships and this backend must not copy. See ProgramModelProbe for the harness.

using System.Text.RegularExpressions;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

public sealed partial class RestatedDefaultEmissionTests : ProgramModelProbe
{
    public RestatedDefaultEmissionTests()
        : base("MistRestatedDefault_")
    {
    }

    protected override string Fixtures { get; } = """
        public abstract Holder<T>
        {
            T Item();

            T Same()
            {
                T held = Item();
                return held;
            }
        }

        public class IntHolder : Holder<int>
        {
            public int Item()
            {
                return 5;
            }
        }

        public class Wrapper<U> : Holder<U>
        {
            private U held;

            public Wrapper(U held)
            {
                this.held = held;
            }

            public U Item()
            {
                return held;
            }
        }
        """;

    protected override string Preamble { get; } = "";

    [GeneratedRegex(@"\bT\b")]
    private static partial Regex AbstractParameter();

    private string EmitType(string fullyQualifiedName)
    {
        var model = BuildModel(statement: "", extraTypes: "", extraPreamble: "", out _);
        var type = Assert.IsAssignableFrom<DeclaredType>(model.TypesByFQN[fullyQualifiedName]);
        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        var anyEmitted = false;

        TypeEmitter.Emit(type, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, writer, diagnostics, ref anyEmitted);

        return writer.ToString();
    }

    [Fact]
    public void ADefaultIsRestatedWithTheConformerArgumentInSignatureAndBody()
    {
        var text = EmitType("Probe.IntHolder");

        Assert.Contains("public int Same()", text);
        Assert.Contains("int held = ", text);
        Assert.DoesNotMatch(AbstractParameter(), text);
    }

    [Fact]
    public void AGenericConformerRestatesUnderItsOwnParameter()
    {
        var text = EmitType("Probe.Wrapper<>");

        Assert.Contains("public U Same()", text);
        Assert.Contains("U held = ", text);
        Assert.DoesNotMatch(AbstractParameter(), text);
    }
}

public sealed class RestatedDefaultEntryEmissionTests
{
    [Theory]
    [Trait("Tier", "2")]
    [InlineData("interface", "T")]
    [InlineData("concrete", "int")]
    [InlineData("generic", "U")]
    public void ADefaultReadsItsClausesAtEntryUnderTheTypeThatOwnsTheEmittedBody(string route, string expectedType)
    {
        var source = new AbstractType("Source<>", "Probe", null, null, null);
        var parameter = new TypeParameter("T", 0, source.Scope, source);
        source.TypeParametersList.Add(parameter);
        var handle = new ClassType("Handle", "Probe", null, null, null)
        {
            Modifiers = new MemberModifiers { Discipline = SubstructuralDiscipline.Linear },
        };
        handle.MethodsList.Add(new Method("Drop", new ClassType("Void", "Standard", null, null, null), [], default));
        var body = new Block(null!)
        {
            Statements = [
                new Block(null!)
                {
                    Statements = [
                        new LocalDeclarationStatement(new LocalVariable("held", handle), null),
                        new LocalDeclarationStatement(new LocalVariable("value", parameter), null),
                    ],
                },
                new ReturnStatement(new LocalReferenceExpression(null!, new LocalVariable("value", parameter)), parameter),
            ],
        };
        var method = new Method("Read", parameter, [new Parameter("held", handle)], default) { Body = body, DeclaringType = source };
        method.ContextBindingsList.Add(new ContextBinding(parameter, "value"));
        method.ContextBindingsList.Add(new ContextBinding(new ClassType("String", "Standard", null, null, null), "ignored"));
        source.MethodsList.Add(method);
        MemberType emitted = source;

        if(route != "interface")
        {
            var conformer = new ClassType(route == "generic" ? "Wrapper<>" : "Reader", "Probe", null, null, null);
            var argument = route == "generic"
                ? (MistType)new TypeParameter("U", 0, conformer.Scope, conformer)
                : new ClassType("Int32", "Standard", null, null, null);

            if(argument is TypeParameter ownParameter)
            {
                conformer.TypeParametersList.Add(ownParameter);
            }

            conformer.BaseAbstractTypes.Add(new ConstructedType(source, [argument]));
            emitted = conformer;
        }

        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        ContextLowering.Reset();

        TypeEmitter.EmitTypeBody(emitted, new ConstraintClosureMap(), new VariadicUseRegistry(), writer, diagnostics);

        var text = writer.ToString();
        Assert.Contains($"{expectedType} Read(Handle held)", text);
        Assert.Contains($"var value = ({expectedType})MistContext.Peek<{expectedType}>();", text);
        Assert.Contains("var ignored = (string)MistContext.Peek<string>();", text);
        Assert.True(text.IndexOf("MistContext.Peek", StringComparison.Ordinal)
            < text.IndexOf("return value;", StringComparison.Ordinal));
        Assert.Contains("Handle held_1;", text);
        Assert.Contains($"{expectedType} value_1;", text);
        Assert.Contains("finally", text);
        Assert.Contains("held.Drop();", text);
        Assert.True(ContextLowering.Used());
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Theory]
    [Trait("Tier", "2")]
    [InlineData(false)]
    [InlineData(true)]
    public void ADefaultWithNoClauseHasNoContextLookup(bool restated)
    {
        var unit = new ClassType("Void", "Standard", null, null, null);
        var source = new AbstractType("Source", "Probe", null, null, null);
        source.MethodsList.Add(new Method("Read", unit, [], default)
        {
            Body = new Block(null!),
            DeclaringType = source,
        });
        MemberType emitted = source;

        if(restated)
        {
            var conformer = new ClassType("Reader", "Probe", null, null, null);
            conformer.BaseAbstractTypes.Add(source);
            emitted = conformer;
        }

        var writer = new CSharpWriter();
        var diagnostics = new DiagnosticBag();
        ContextLowering.Reset();

        TypeEmitter.EmitTypeBody(emitted, new ConstraintClosureMap(), new VariadicUseRegistry(), writer, diagnostics);

        Assert.Contains("void Read()", writer.ToString());
        Assert.DoesNotContain("MistContext.Peek", writer.ToString());
        Assert.False(ContextLowering.Used());
        Assert.Empty(diagnostics.ToImmutableArray());
    }
}