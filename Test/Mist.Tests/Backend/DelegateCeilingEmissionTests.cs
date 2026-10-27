using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Common.Source;
using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class DelegateCeilingEmissionTests
{
    private static readonly Lazy<ProgramModel.Model.ProgramModel> Model = new(BuildModel);

    [Fact]
    public void AFunctionWrittenPastTheCeiling_IsRefusedAtItsDeclaration_ThoughNothingInstantiatesIt()
    {
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);

        using(DelegateCeiling.Enter(Model.Value, diagnostics))
        {
        }

        var refusal = Assert.Single(diagnostics.ToImmutableArray());

        Assert.Equal(("MIST0028", PipelineStage.Backend, 11), (refusal.Number.Value, refusal.Stage, refusal.Position().Start.Line));
        Assert.Contains("takes 17 inputs, and System.Func and System.Action hold at most 16", refusal.Message);
    }

    [Fact]
    public void AFunctionAPackWidensPastTheCeiling_IsRefusedAtTheUseThatFixedTheWidth()
    {
        var refusal = Assert.Single(Emit("Bundle", width: 17), diagnostic => diagnostic.Position().Start.Line != 11);

        Assert.Equal(("MIST0028", 23), (refusal.Number.Value, refusal.Position().Start.Line));
        Assert.Contains("in 'Bundle.Map', where 'TArgs' holds 17 types, takes 17 inputs", refusal.Message);
        Assert.Contains("=17", Assert.IsType<SymbolValue>(refusal.Arguments[0]).Identity);
        Assert.Equal(17, Assert.IsType<QuantityValue>(refusal.Arguments[1]).Value);
    }

    [Fact]
    public void AFunctionRefusedWhereItIsWritten_IsNotRefusedAgainWhereItIsEmitted()
    {
        Assert.Equal(11, Assert.Single(Emit("Never", width: 2)).Position().Start.Line);
    }

    [Theory]
    [InlineData("Bundle", 16)]
    [InlineData("Bundle", 0)]
    [InlineData("Narrow", 17)]
    public void AFunctionWithinTheCeiling_IsEmitted(string typeName, int width)
    {
        Assert.DoesNotContain(Emit(typeName, width), diagnostic => diagnostic.Position().Start.Line != 11);
    }

    private static IReadOnlyList<Diagnostic> Emit(string typeName, int width)
    {
        var model = Model.Value;
        var diagnostics = new DiagnosticBag(PipelineStage.Backend);
        var anyEmitted = false;
        var declared = model.TypesByFQN.Values.OfType<MemberType>()
            .Single(type => type.NamespaceName == "Probe" && MistType.StripArity(type.Name) == typeName);

        using(DelegateCeiling.Enter(model, diagnostics))
        using(Monomorph.For(declared.TypeParametersList, [width])?.Enter())
        {
            TypeEmitter.Emit(
                declared, ConstraintClosureMap.Build(model, diagnostics), model.VariadicUses, new CSharpWriter(), diagnostics, ref anyEmitted);
        }

        return diagnostics.ToImmutableArray();
    }

    private static ProgramModel.Model.ProgramModel BuildModel()
    {
        var built = new HandBuiltModel();
        var model = built.Model;
        var self = model.AddSelfAssembly("Probe", isLibrary: true);
        var unit = new CompilationUnit { SourcePath = "Probe.mist" };
        self.CompilationUnits.Add(unit);

        foreach(var (name, inputs, line) in new[] { ("Bundle", -1, 3), ("Never", 17, 11), ("Narrow", 16, 15) })
        {
            var type = new ClassType(name + "<>", "Probe", new Scope(null), null, null) { Assembly = self };
            var pack = new TypeParameter("TArgs", 0, type.Scope, type) { PackKind = TypePackKind.Unbracketed };
            type.TypeParametersList.Add(pack);
            var result = inputs < 0 ? new TypeParameter("TResult", 0, new Scope(type.Scope), type) : (IMistType)built.Type("Int32");
            var function = new FunctionType(inputs < 0 ? [new SpreadType(pack)] : [.. Enumerable.Repeat<IMistType>(built.Type("Int32"), inputs)], result);
            var method = new Method(inputs < 0 ? "Map" : "Take", result, [new Parameter("f", function)],
                new MemberModifiers { Accessibility = Accessibility.Public })
            {
                DeclaringType = type,
                DeclarationNode = AtLine(line),
                Body = HandBuiltModel.Block(),
            };
            if(result is TypeParameter parameter)
            {
                parameter.DeclaringMethod = method;
                method.TypeParametersList.Add(parameter);
            }

            type.MethodsList.Add(method);
            unit.Types.Add(type);
            _ = model.RegisterType(type);
            if(inputs < 0)
            {
                foreach(var width in new[] { 16, 17 })
                {
                    var value = new TypePackValue([.. Enumerable.Repeat<IMistType>(built.Type("Int32"), width)]);
                    model.ConstructorConstraintClosures.RecordType(type, [new LiftedType(built.Type("Type"), value)], AtLine(width + 6));
                }
            }
        }

        return model;
    }

    private static Storm.Runtime.SyntaxNode AtLine(int line)
    {
        var text = new SourceText("Probe.mist", new string('\n', line - 1) + "x");
        return new TrueLiteralExpressionPrimaryNode
        {
            Span = new Storm.Runtime.SourceSpan(line - 1, 1),
            Origin = new ParsedOrigin(text),
        };
    }
}