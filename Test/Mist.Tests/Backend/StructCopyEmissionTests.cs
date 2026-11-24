using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;
using Mist.Tests.ProgramAnalysis;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class StructCopyEmissionTests
{
    [Theory]
    [InlineData("mixed")]
    [InlineData("nested")]
    [InlineData("nested-tuple")]
    [InlineData("tuple")]
    [InlineData("optional")]
    [InlineData("generic")]
    [InlineData("generic-nested")]
    [InlineData("private-readonly")]
    public void AConstructionCopiesOnlyOwnedObjectsAndReachesTheirClone(string shape)
    {
        var (type, clones) = StructCopyDecisionTests.Mixed(shape);
        var model = new HandBuiltModel();
        model.Model.Clones = clones;
        var assembly = model.Model.AddSelfAssembly("Probe");
        var unit = new CompilationUnit { SourcePath = "hand-built.mist" };
        assembly.CompilationUnits.Add(unit);
        var declared = new HashSet<ConcreteType>(ReferenceEqualityComparer.Instance);

        void add(IMistType held)
        {
            if(held is ConstructedType construction)
            {
                foreach(var argument in construction.TypeArguments)
                {
                    add(argument);
                }
            }

            if((held is ConstructedType closed ? closed.Definition : held) is ConcreteType concrete && concrete.DeclarationNode is not null && declared.Add(concrete))
            {
                concrete.Assembly = assembly;
                unit.Types.Add(concrete);
                _ = model.Model.RegisterType(concrete);
                foreach(var field in concrete.FieldsList)
                {
                    add(field.Type);
                }
            }
            else if(held is ITupleType tuple)
            {
                foreach(var element in tuple.ElementTypes)
                {
                    add(element);
                }
            }
            else if(held is UnionType union)
            {
                foreach(var element in union.Constituents)
                {
                    add(element);
                }
            }
        }

        add(type);
        var style = declared.Single(candidate => candidate.Name == "Style");
        style.DerivedClone = new Method("Clone", style, [], DeclarationFragments.Public with { IsOwned = true }) { DeclaringType = style, IsDerivedClone = true };
        style.MethodsList.Add(style.DerivedClone);
        var source = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("shared", type));
        var copy = new CloneExpression(source.Node, source, copiesOwnedFields: true);
        var probe = new Method("Run", model.Type("Void"), [], DeclarationFragments.Public)
        {
            DeclaringType = style,
            Body = HandBuiltModel.Block(new ExpressionStatement(copy)),
        };
        style.MethodsList.Add(probe);
        var diagnostics = new DiagnosticBag();

        using(CloneDemand.Enter(model.Model))
        {
            var expression = BodyEmitter.Render(copy, diagnostics);
            Assert.Equal("shared.__CopyOwned()", expression);
            Assert.True(CloneDemand.Emits(style));
            foreach(var structure in declared.OfType<StructType>())
            {
                Assert.True(CloneDemand.Constructs(structure));
                var writer = new CSharpWriter();
                StructCopyEmitter.Emit(structure, writer, diagnostics);
                var emitted = writer.ToString();
                Assert.Contains("var copy = this;", emitted);
                Assert.DoesNotContain("copy.Plain =", emitted);
                if(structure.Name == "Wrapper<>")
                {
                    Assert.Contains("copy.Inner = __ConstructSlot(copy.Inner);", emitted);
                }
                else if(structure.Name == "Wrapper")
                {
                    Assert.Contains(shape == "nested-tuple"
                        ? "copy.Inner = (copy.Inner.Item1.__CopyOwned(), copy.Inner.Item2);"
                        : "copy.Inner = copy.Inner.__CopyOwned();", emitted);
                }
                else if(shape == "generic")
                {
                    Assert.Contains("copy.Own = __CloneSlot(copy.Own);", emitted);
                }
                else if(shape == "private-readonly")
                {
                    Assert.Contains("Unsafe.AsRef(in copy.Own) = copy.Own?.Clone()!;", emitted);
                }
                else if(shape == "tuple")
                {
                    Assert.Contains("copy.Own.Item1?.Clone()!", emitted);
                    Assert.Contains("copy.Own.Item2", emitted);
                }
                else
                {
                    Assert.Contains(".Clone()", emitted);
                }
            }
        }

        Assert.Empty(diagnostics.ToImmutableArray());
    }
}