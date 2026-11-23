using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Backend;

[Trait("Tier", "3")]
public sealed class GenericCloneEmissionTests
{
    [Theory]
    [InlineData("member")]
    [InlineData("subclass")]
    [InlineData("generic")]
    [InlineData("changeable")]
    public void GenericSlotFallbackCarriesTheCompilerUnchangeabilityDecision(string shape)
    {
        var model = new HandBuiltModel();
        var assembly = model.Model.AddSelfAssembly("Probe");
        var unit = new CompilationUnit { SourcePath = "hand-built.mist" };
        assembly.CompilationUnits.Add(unit);

        ClassType declare(string name, bool isReadonly)
        {
            var type = new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
            {
                Modifiers = DeclarationFragments.Public with { IsReadonly = isReadonly },
                Assembly = assembly,
            };
            unit.Types.Add(type);
            _ = model.Model.RegisterType(type);
            return type;
        }

        var int32 = new StructType("Int32", "Standard", new Scope(null), null, null);
        var regex = new ClassType("Regex", "Standard", new Scope(null), null, null)
        {
            Modifiers = DeclarationFragments.Public with { IsReadonly = true, IsIntrinsic = true },
            Assembly = assembly,
        };
        unit.Types.Add(regex);
        _ = model.Model.RegisterType(regex);
        var frozen = declare(shape == "generic" ? "FrozenValue<>" : "FrozenValue", shape != "changeable");
        IMistType contents = shape == "generic" ? DeclarationFragments.Parameter(frozen, frozen.Scope, "T") : int32;
        frozen.FieldsList.Add(new Field(shape == "subclass" ? "Value" : "Clone", contents, OwnershipKind.Unrestricted, DeclarationFragments.Public with { IsReadonly = true }) { DeclaringType = frozen });

        if(shape == "subclass")
        {
            var child = declare("FrozenChild", true);
            child.BaseType = frozen;
            child.MethodsList.Add(new Method("Clone", child, [], DeclarationFragments.Public with { IsOwned = true }) { DeclaringType = child });
        }

        var holder = declare("Holder<>", false);
        var parameter = DeclarationFragments.Parameter(holder, holder.Scope, "T");
        holder.FieldsList.Add(new Field("Item", parameter, OwnershipKind.Owned, DeclarationFragments.Public) { DeclaringType = holder });
        holder.DerivedClone = new Method("Clone", model.Interner.GetOrCreateConstructedType(holder, [parameter]), [], DeclarationFragments.Public)
        {
            DeclaringType = holder,
            IsDerivedClone = true,
        };
        model.Model.Clones = new CloneDerivation(unit.Types);
        var value = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("shared", holder));
        holder.MethodsList.Add(new Method("Run", model.Type("Void"), [], DeclarationFragments.Public)
        {
            DeclaringType = holder,
            Body = HandBuiltModel.Block(new ExpressionStatement(new CloneExpression(HandBuiltModel.Node(), value))),
        });
        var diagnostics = new DiagnosticBag();

        using(CloneDemand.Enter(model.Model))
        {
            var closures = ConstraintClosureMap.Build(model.Model, diagnostics);
            var frozenWriter = new CSharpWriter();
            TypeEmitter.EmitTypeBody(frozen, closures, model.Model.VariadicUses, frozenWriter, diagnostics);
            var predicate = "private static bool __MistUnchangeable(System.Func<System.Type, bool> judge)";

            if(shape == "changeable")
            {
                Assert.DoesNotContain(predicate, frozenWriter.ToString());
            }
            else
            {
                Assert.Contains(predicate, frozenWriter.ToString());
                Assert.Contains(shape == "generic" ? "return judge(typeof(T));" : "return true;", frozenWriter.ToString());
                Assert.DoesNotContain("public Probe.FrozenValue Clone()", frozenWriter.ToString());
            }

            var holderWriter = new CSharpWriter();
            TypeEmitter.EmitTypeBody(holder, closures, model.Model.VariadicUses, holderWriter, diagnostics);
            Assert.Contains("type.GetMethod(\"__MistUnchangeable\", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.DeclaredOnly)", holderWriter.ToString());
            Assert.Contains("System.Func<System.Type, bool> judge = __Unchangeable;", holderWriter.ToString());
            Assert.Contains("type == typeof(System.Text.RegularExpressions.Regex)", holderWriter.ToString());
        }

        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("inferred")]
    [InlineData("explicit")]
    [InlineData("forwarded")]
    [InlineData("nested")]
    [InlineData("optional")]
    [InlineData("multiple-optionals")]
    public void AGenericCopyEmitsTheCloneOfEveryChangeableArgumentWithoutDependingOnWrittenClosings(string shape)
    {
        var model = new HandBuiltModel();
        var assembly = model.Model.AddSelfAssembly("Probe");
        var unit = new CompilationUnit { SourcePath = "hand-built.mist" };
        assembly.CompilationUnits.Add(unit);
        var clonable = new AbstractType("IClonable<>", "Standard", new Scope(null), null, null);
        _ = DeclarationFragments.Parameter(clonable, clonable.Scope, "T");
        _ = model.Model.RegisterType(clonable);

        ClassType declare(string name)
        {
            var type = new ClassType(name, "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
            {
                Modifiers = DeclarationFragments.Public,
                Assembly = assembly,
            };
            unit.Types.Add(type);
            _ = model.Model.RegisterType(type);
            return type;
        }

        var style = declare("Style");
        var int32 = new StructType("Int32", "Standard", new Scope(null), null, null);
        style.FieldsList.Add(new Field("Size", int32, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = style });
        var holder = declare("Holder<>");
        var t = DeclarationFragments.Parameter(holder, holder.Scope, "T");
        var slotType = shape is "optional" or "multiple-optionals" ? model.Interner.GetOrCreateUnionType([t, model.Type("Void")]) : t;
        holder.FieldsList.Add(new Field("Item", slotType, OwnershipKind.Owned, DeclarationFragments.Public) { DeclaringType = holder });

        if(shape == "multiple-optionals")
        {
            holder.FieldsList.Add(new Field("Other", slotType, OwnershipKind.Owned, DeclarationFragments.Public) { DeclaringType = holder });
        }

        var holderOfStyle = model.Interner.GetOrCreateConstructedType(holder, [style]);
        IMistType copied = holderOfStyle;
        var probe = declare("Program");
        var wrap = DeclarationFragments.Method(probe, "Wrap", holder);
        var u = DeclarationFragments.Parameter(wrap, probe.Scope, "U");
        ClassType? packType = null;
        StructType? stackType = null;

        if(shape == "explicit")
        {
            model.Model.ConstructorConstraintClosures.RecordMethod(wrap, [style], TypeSubstitution.Empty.With(u, style), HandBuiltModel.Node());
            packType = declare("Pack<>");
            DeclarationFragments.Parameter(packType, packType.Scope, "Ts").PackKind = TypePackKind.Unbracketed;
        }

        if(shape == "inferred")
        {
            stackType = new StructType("Lease", "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>())
            {
                Modifiers = DeclarationFragments.Public with { IsRef = true },
                Assembly = assembly,
            };
            unit.Types.Add(stackType);
            _ = model.Model.RegisterType(stackType);
        }

        if(shape == "forwarded")
        {
            var pair = declare("Pair<>");
            var v = DeclarationFragments.Parameter(pair, pair.Scope, "T");
            pair.FieldsList.Add(new Field("Inner", model.Interner.GetOrCreateConstructedType(holder, [v]), OwnershipKind.Owned, DeclarationFragments.Public) { DeclaringType = pair });
            model.Model.ConstructorConstraintClosures.RecordType(holder, [v], HandBuiltModel.Node());
            model.Model.ConstructorConstraintClosures.RecordType(pair, [style], HandBuiltModel.Node());
            copied = model.Interner.GetOrCreateConstructedType(pair, [style]);
        }
        else if(shape == "nested")
        {
            copied = model.Interner.GetOrCreateConstructedType(holder, [holderOfStyle]);
        }

        foreach(var type in unit.Types.OfType<ConcreteType>())
        {
            IMistType self = type.TypeParametersList.Count == 0 ? type : model.Interner.GetOrCreateConstructedType(type, type.TypeParametersList);
            type.DerivedClone = new Method("Clone", self, [], DeclarationFragments.Public with { IsOwned = true, IsReadonly = true })
            {
                DeclaringType = type,
                IsDerivedClone = true,
            };
            var conformance = model.Interner.GetOrCreateConstructedType(clonable, [self]);
            type.DerivedCloneConformance = conformance;
            type.BaseAbstractTypes.Add(conformance);
        }

        model.Model.Clones = new CloneDerivation(unit.Types);
        var value = new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("shared", copied));
        probe.MethodsList.Add(new Method("Run", model.Type("Void"), [], DeclarationFragments.Public)
        {
            DeclaringType = probe,
            Body = HandBuiltModel.Block(new ExpressionStatement(new CloneExpression(HandBuiltModel.Node(), value))),
        });
        var diagnostics = new DiagnosticBag();

        using(CloneDemand.Enter(model.Model))
        {
            var closures = ConstraintClosureMap.Build(model.Model, diagnostics);
            var styleWriter = new CSharpWriter();
            TypeEmitter.EmitTypeBody(style, closures, model.Model.VariadicUses, styleWriter, diagnostics);
            Assert.Contains("public Probe.Style Clone()", styleWriter.ToString());
            Assert.Contains("Standard.IClonable<Probe.Style>", styleWriter.ToString());
            var holderWriter = new CSharpWriter();
            TypeEmitter.EmitTypeBody(holder, closures, model.Model.VariadicUses, holderWriter, diagnostics);
            Assert.Contains("public Probe.Holder<T> Clone()", holderWriter.ToString());
            Assert.Contains("__CloneSlot(", holderWriter.ToString());
            Assert.Contains("is Standard.IClonable<TValue>", holderWriter.ToString());
            Assert.Contains("return (TValue)__CloneBox(value, typeof(TValue));", holderWriter.ToString());
            Assert.Contains("field.SetValue(value, __CloneBox(field.GetValue(value), field.FieldType));", holderWriter.ToString());
            Assert.Contains("if(__Unchangeable(type)) return value;", holderWriter.ToString());
            Assert.Contains("throw new System.InvalidOperationException", holderWriter.ToString());

            if(shape is "optional" or "multiple-optionals")
            {
                Assert.Contains("this.Item is { } __present0", holderWriter.ToString());
            }

            if(shape == "multiple-optionals")
            {
                Assert.Contains("this.Other is { } __present1", holderWriter.ToString());
            }

            if(shape == "forwarded")
            {
                var pairWriter = new CSharpWriter();
                TypeEmitter.EmitTypeBody((ConcreteType)((ConstructedType)copied).Definition, closures, model.Model.VariadicUses, pairWriter, diagnostics);
                Assert.Contains("this.Inner = this.Inner?.Clone()!;", pairWriter.ToString());
            }

            if(shape == "inferred")
            {
                var tuple = model.Interner.GetOrCreateTupleType([style, int32]);
                var union = Assert.IsType<UnionType>(model.Interner.GetOrCreateUnionType([tuple, int32]));
                var carrier = UnionCarrierNames.CarrierName(union);
                var unionWriter = new CSharpWriter();
                UnionCarrierEmitter.Emit(carrier, union, new Dictionary<string, UnionType> { [carrier] = union }, unionWriter);
                Assert.Contains($"public {carrier} Clone()", unionWriter.ToString());
                Assert.Contains(".Item1?.Clone()!", unionWriter.ToString());
            }

            if(packType is not null)
            {
                using var monomorph = Monomorph.For(packType.TypeParametersList, [2])!.Enter();
                var packWriter = new CSharpWriter();
                TypeEmitter.EmitTypeBody(packType, closures, model.Model.VariadicUses, packWriter, diagnostics);
                Assert.Contains("public Pack_2<T1, T2> Clone()", packWriter.ToString());
                Assert.Contains("Standard.IClonable<Pack_2<T1, T2>>", packWriter.ToString());
            }

            if(stackType is not null)
            {
                var stackWriter = new CSharpWriter();
                TypeEmitter.EmitTypeBody(stackType, closures, model.Model.VariadicUses, stackWriter, diagnostics);
                Assert.DoesNotContain("IClonable", stackWriter.ToString());
                Assert.DoesNotContain("Clone()", stackWriter.ToString());
            }
        }

        Assert.Empty(diagnostics.ToImmutableArray());
    }
}