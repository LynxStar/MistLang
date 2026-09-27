using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PurityFragments
{
    private IReadOnlyList<Diagnostic> RoundFiveSites(string name)
    {
        return name switch
        {
            "static-method-group-of-a-noisy-type-to-a-pure-function-type" => NoisyTypeUsed(NoisySite.MethodGroup),
            "static-computed-property-of-a-noisy-type" => NoisyTypeUsed(NoisySite.ComputedProperty),
            "user-operator-of-a-noisy-type" => NoisyTypeUsed(NoisySite.Operator),
            "user-conversion-of-a-noisy-type" => NoisyTypeUsed(NoisySite.Conversion),
            "default-of-a-type-with-a-noisy-static-constructor" => NoisyTypeUsed(NoisySite.Default),
            "static-call-to-another-instantiation-of-the-scopes-generic-type" => NoisyTypeUsed(NoisySite.OtherInstantiation),
            "static-call-to-the-scopes-own-instantiation" => NoisyTypeUsed(NoisySite.OwnInstantiation),
            "default-of-a-default-constrained-type-parameter" => DefaultOfADefaultConstrainedTypeParameter(new PurityAnalyzer(), PureStatic),
            "readonly-default-of-a-default-constrained-type-parameter" => DefaultOfADefaultConstrainedTypeParameter(new ReadonlyAnalyzer(), ReadonlyStatic),
            "enum-looked-up-from-its-underlying-value" => EnumLookedUpFromItsUnderlyingValue(),
            "method-group-with-a-context-clause-to-a-pure-function-type" => MethodGroupWithAContextClause(),
            "clr-interface-slot-with-no-mist-implementer" => ClrInterfaceSlotWithNoMistImplementer(),
            "list-of-optional-struct-compared-through-its-iequatable" => ListOfOptionalStructCompared(),
            "deconstruction-into-a-fresh-local-from-an-input" => Deconstruction(DeconstructionSite.FreshLocal),
            "deconstruction-into-a-field-of-a-frozen-input" => Deconstruction(DeconstructionSite.FrozenField),
            "deconstruction-into-a-captured-variable-of-a-pure-lambda" => Deconstruction(DeconstructionSite.CapturedWrite),
            "deconstruction-reassigning-a-captured-variable-after-the-capture" => Deconstruction(DeconstructionSite.CapturedReassigned),
            "deconstruction-swapping-a-held-lambda" => Deconstruction(DeconstructionSite.HeldLambda),
            "deconstruction-into-a-field-from-a-readonly-member" => Deconstruction(DeconstructionSite.ReadonlyField),
            "constructor-writing-another-instances-field-called-from-a-readonly-member" => ConstructorPrivilegeOverAnotherInstance(),
            "readonly-member-interpolating-a-mist-iformattable" => ReadonlyMemberInterpolatingAMistIFormattable(),
            "readonly-member-reading-a-static-with-a-noisy-initializer" => ReadonlyMemberReadingANoisyStatic(),
            "readonly-member-calling-a-plain-function-value" => ReadonlyMemberCallingAFunctionValue(pure: false),
            "readonly-member-calling-a-pure-function-value" => ReadonlyMemberCallingAFunctionValue(pure: true),
            "readonly-struct-member-assigning-this" => StructMemberAssigningThis(Public with { IsReadonly = true }, new ReadonlyAnalyzer()),
            "pure-struct-member-assigning-this" => StructMemberAssigningThis(Pure, new PurityAnalyzer()),
            "local-struct-member-assigning-this" => StructMemberAssigningThis(Local, new PurityAnalyzer()),
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };
    }

    public bool UnmarkedStructMemberAssigningThisIsInferredReadonly()
    {
        var counter = HandBuiltModel.Struct("S");
        _ = Field(counter, "V", integer);
        var next = Method(counter, "Next", unit, Public, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new ThisExpression(Node(), counter), Fresh(counter)))));
        return new ReadonlyInference().IsReadonly(next);
    }

    private enum NoisySite
    {
        MethodGroup,
        ComputedProperty,
        Operator,
        Conversion,
        Default,
        OtherInstantiation,
        OwnInstantiation,
    }

    private enum DeconstructionSite
    {
        FreshLocal,
        FrozenField,
        CapturedWrite,
        CapturedReassigned,
        HeldLambda,
        ReadonlyField,
    }

    private IReadOnlyList<Diagnostic> NoisyTypeUsed(NoisySite site)
    {
        var noisy = site is NoisySite.Operator or NoisySite.Conversion ? (ConcreteType)HandBuiltModel.Struct("Noisy") : HandBuiltModel.Class("Noisy");
        noisy.ConstructorsList.Add(new Constructor("Noisy", noisy, [], Static) { DeclaringType = noisy, Body = HandBuiltModel.Block(WriteStatic(ticks)) });

        switch(site)
        {
            case NoisySite.MethodGroup:
                var x = Input("x", integer);
                var inc = Method(noisy, "Inc", integer, PureStatic, Returning(Ref(x)), x);
                var step = Function(pure: true, integer, integer);
                var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
                    new LocalDeclarationStatement(new LocalVariable("f", step), new FunctionReferenceExpression(Node(37), new TypeReferenceExpression(Node(), noisy), inc, step))));
                return Judge(new PurityAnalyzer(), main);

            case NoisySite.ComputedProperty:
                var seed = new Property("Seed", integer, PureStatic) { DeclaringType = noisy, Getter = new PropertyAccessor { Body = Returning(Literal(integer, 7L)) } };
                noisy.PropertiesList.Add(seed);
                var read = new MemberAccessExpression(Node(37), new TypeReferenceExpression(Node(), noisy), "Seed", integer, member: new PropertyResult(seed, noisy, []));
                return Judge(new PurityAnalyzer(), Method(probe, "ReadB", integer, PureStatic, Returning(read)));

            case NoisySite.Operator:
                var plus = new Operator(OperatorKind.Plus, integer, [new Parameter("a", noisy), new Parameter("b", noisy)], PureStatic)
                {
                    DeclaringType = noisy,
                    Body = Returning(Literal(integer, 0L)),
                };
                noisy.OperatorsList.Add(plus);
                var a = Input("a", noisy);
                var sum = new BinaryExpression(Node(37), Ref(a), BinaryOperatorKind.Add, Ref(a), integer) { Operator = plus };
                return Judge(new PurityAnalyzer(), Method(probe, "SumC", integer, PureStatic, Returning(sum), a));

            case NoisySite.Conversion:
                var conversion = new CastOperator(integer, new Parameter("d", noisy), true, PureStatic) { DeclaringType = noisy, Body = Returning(Literal(integer, 0L)) };
                noisy.CastOperatorsList.Add(conversion);
                var d = Input("d", noisy);
                var converted = new ImplicitConversionExpression(Node(37), Ref(d), integer, [new ConversionFunction(conversion, noisy, integer)]);
                return Judge(new PurityAnalyzer(), Method(probe, "ConvD", integer, PureStatic, Returning(converted), d));

            case NoisySite.Default:
                var fallback = new LocalVariable("s", noisy);
                return Judge(new PurityAnalyzer(), Method(probe, "ReadDefault", integer, PureStatic, HandBuiltModel.Block(
                    new LocalDeclarationStatement(fallback, new DefaultExpression(Node(37), noisy)),
                    new ReturnStatement(Literal(integer, 0L), integer))));

            default:
                var parameter = new TypeParameter("T", 0, noisy.Scope, noisy);
                noisy.TypeParametersList.Add(parameter);
                var size = Method(noisy, "Size", integer, PureStatic, Returning(Literal(integer, 1L)));
                var named = model.Interner.GetOrCreateConstructedType(noisy, [site is NoisySite.OtherInstantiation ? text : parameter]);
                var peek = Method(noisy, "Peek", integer, PureStatic, Returning(Call(size, new TypeReferenceExpression(Node(), named), 37)));
                return Judge(new PurityAnalyzer(), peek);
        }
    }

    private IReadOnlyList<Diagnostic> DefaultOfADefaultConstrainedTypeParameter(IProgramAnalyzer analyzer, MemberModifiers modifiers)
    {
        var parameter = new TypeParameter("T", 0, probe.Scope, probe);
        parameter.ConstraintsList.Add(new KeywordConstraint(TypeParameterConstraintKind.Default));
        var made = new LocalVariable("made", parameter);
        var make = Method(probe, "Make", integer, modifiers, HandBuiltModel.Block(
            new LocalDeclarationStatement(made, new DefaultExpression(Node(37), parameter)),
            new ReturnStatement(Literal(integer, 1L), integer)));
        make.TypeParametersList.Add(parameter);
        return Judge(analyzer, make);
    }

    private IReadOnlyList<Diagnostic> EnumLookedUpFromItsUnderlyingValue()
    {
        var seeds = HandBuiltModel.Class("Seeds");
        var loud = Method(seeds, "Loud", integer, Static, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 7L), integer)));
        var level = HandBuiltModel.Enum("Level", integer);
        level.MembersList.Add(new EnumMember("Low", false, Call(loud, null, 0)));
        var found = model.Interner.GetOrCreateUnionType([level, unit]);
        var raw = Input("raw", integer);
        var lookup = new CastExpression(Node(37), Ref(raw), level, [], found);
        return Judge(new PurityAnalyzer(), Method(probe, "FromRaw", found, PureStatic, Returning(lookup), raw));
    }

    private IReadOnlyList<Diagnostic> MethodGroupWithAContextClause()
    {
        var x = Input("x", integer);
        var scaled = Method(probe, "Scaled", integer, PureStatic, Returning(Ref(x)), x);
        scaled.ContextBindingsList.Add(new ContextBinding(integer, "factor"));
        var step = Function(pure: true, integer, integer);
        var main = Method(probe, "Main", unit, Static, HandBuiltModel.Block(
            new LocalDeclarationStatement(new LocalVariable("s", step), new FunctionReferenceExpression(Node(37), new TypeReferenceExpression(Node(), probe), scaled, step))));
        return Judge(new PurityAnalyzer(), main);
    }

    private IReadOnlyList<Diagnostic> ClrInterfaceSlotWithNoMistImplementer()
    {
        var collection = new AbstractType("ICollection", "System.Collections.Generic", new Scope(null), null, null);
        var add = Method(collection, "Add", unit, Public with { IsClrProjected = true }, null, Input("item", integer));
        var c = Input("c", collection);
        var push = Method(probe, "Push", integer, PureStatic, HandBuiltModel.Block(
            new ExpressionStatement(Call(add, Ref(c), 37, Literal(integer, 1L))),
            new ReturnStatement(Literal(integer, 0L), integer)), c);
        return Judge(new PurityAnalyzer(), push, collection);
    }

    private IReadOnlyList<Diagnostic> ListOfOptionalStructCompared()
    {
        var loud = new StructType("Loud", "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(40));
        loud.BaseAbstractTypes.Add(new AbstractType("IEquatable<>", "System", new Scope(null), null, null));
        _ = Method(loud, "Equals", boolean, Public, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(boolean, true), boolean)), Input("other", loud));
        var list = model.Type("List<>");
        list.Modifiers = Public with { IsIntrinsic = true };
        var held = new TypeParameter("T", 0, list.Scope, list);
        list.TypeParametersList.Add(held);
        var fresh = new Constructor("List", list, [], Pure with { IsIntrinsic = true }) { DeclaringType = list };
        list.ConstructorsList.Add(fresh);
        var contains = Method(list, "Contains", boolean, LocalReadonly with { IsIntrinsic = true }, null, Input("item", held));
        var optional = model.Interner.GetOrCreateUnionType([loud, unit]);
        var closed = model.Interner.GetOrCreateConstructedType(list, [optional]);
        var l = new LocalVariable("l", closed);
        var probed = Input("probed", optional);
        var has = Method(probe, "Has", unit, PureStatic, HandBuiltModel.Block(
            new LocalDeclarationStatement(l, new NewExpression(Node(), closed, [], fresh, [], [])),
            new ExpressionStatement(Call(contains, Ref(l), 37, Ref(probed)))), probed);
        return Judge(new PurityAnalyzer(), has, loud);
    }

    private IReadOnlyList<Diagnostic> Deconstruction(DeconstructionSite site)
    {
        var pair = model.Interner.GetOrCreateTupleType([box, integer]);
        var numbers = model.Interner.GetOrCreateTupleType([integer, integer]);
        var t = new LocalVariable("t", integer);

        static TupleLiteralExpression tuple(IMistType type, params Expression[] elements)
        {
            return new TupleLiteralExpression(Node(), elements, [.. elements.Select(_ => (string?)null)], type);
        }

        switch(site)
        {
            case DeconstructionSite.FreshLocal:
                var b = Input("b", box);
                var x = new LocalVariable("x", box);
                return Judge(new PurityAnalyzer(), Method(probe, "Poke", integer, PureStatic, HandBuiltModel.Block(
                    new LocalDeclarationStatement(x, Fresh(box)),
                    new LocalDeclarationStatement(t, Literal(integer, 0L)),
                    new ExpressionStatement(Assign(tuple(pair, Ref(x), Ref(t)), tuple(pair, Ref(b), Literal(integer, 0L)))),
                    new ExpressionStatement(Assign(Access(Ref(x), n, 37), Literal(integer, 1L))),
                    new ReturnStatement(Literal(integer, 0L), integer)), b));

            case DeconstructionSite.FrozenField:
                var lent = Input("b", box, OwnershipKind.Frozen);
                return Judge(new PurityAnalyzer(), Method(probe, "Next", integer, PureStatic, HandBuiltModel.Block(
                    new LocalDeclarationStatement(t, Literal(integer, 0L)),
                    new ExpressionStatement(Assign(tuple(numbers, Access(Ref(lent), n, 37), Ref(t)), tuple(numbers, Literal(integer, 1L), Literal(integer, 0L)))),
                    new ReturnStatement(Literal(integer, 0L), integer)), lent));

            case DeconstructionSite.ReadonlyField:
                var counter = HandBuiltModel.Class("Counter");
                var count = Field(counter, "X", integer);
                return Judge(new ReadonlyAnalyzer(), Method(counter, "Bump", integer, Public with { IsReadonly = true }, HandBuiltModel.Block(
                    new LocalDeclarationStatement(t, Literal(integer, 0L)),
                    new ExpressionStatement(Assign(tuple(numbers, new FieldReferenceExpression(Node(37), count), Ref(t)), tuple(numbers, Literal(integer, 1L), Literal(integer, 0L)))),
                    new ReturnStatement(Literal(integer, 0L), integer))));

            case DeconstructionSite.HeldLambda:
                var plain = Function(pure: false, integer);
                var f = new LocalVariable("f", plain);
                var calm = new LambdaExpression(Node(), [], Literal(integer, 1L), null, false, plain);
                var loud = new LambdaExpression(Node(), [], null, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 2L), integer)), false, plain);
                var swapped = model.Interner.GetOrCreateTupleType([plain, integer]);
                return Judge(new PurityAnalyzer(), Method(probe, "Run", integer, PureStatic, HandBuiltModel.Block(
                    new LocalDeclarationStatement(f, calm),
                    new LocalDeclarationStatement(t, Literal(integer, 0L)),
                    new ExpressionStatement(Assign(tuple(swapped, Ref(f), Ref(t)), tuple(swapped, loud, Literal(integer, 0L)))),
                    new ReturnStatement(new InvocationExpression(Node(37), Ref(f), [], integer, null, []), integer))));

            default:
                var captured = new LocalVariable("n", integer);
                var reader = Function(pure: true, integer);
                var written = site is DeconstructionSite.CapturedWrite;
                var body = written
                    ? HandBuiltModel.Block(
                        new LocalDeclarationStatement(t, Literal(integer, 0L)),
                        new ExpressionStatement(Assign(tuple(numbers, Ref(captured, 37), Ref(t)), tuple(numbers, Literal(integer, 2L), Literal(integer, 0L)))),
                        new ReturnStatement(Literal(integer, 0L), integer))
                    : null;
                var lambda = new LambdaExpression(Node(), [], written ? null : Ref(captured, 37), body, false, reader);
                var m = new LocalVariable("m", integer);
                var statements = new List<Statement>
                {
                    new LocalDeclarationStatement(captured, Literal(integer, 1L)),
                    new LocalDeclarationStatement(m, Literal(integer, 0L)),
                    new LocalDeclarationStatement(new LocalVariable("g", reader), lambda),
                };

                if(!written)
                {
                    statements.Add(new ExpressionStatement(Assign(tuple(numbers, Ref(captured), Ref(m)), tuple(numbers, Literal(integer, 7L), Literal(integer, 8L)))));
                }

                return Judge(new PurityAnalyzer(), Method(probe, "Main", unit, Static, HandBuiltModel.Block([.. statements])));
        }
    }

    private IReadOnlyList<Diagnostic> ConstructorPrivilegeOverAnotherInstance()
    {
        var cellular = HandBuiltModel.Class("Cell");
        var x = Field(cellular, "x", integer);
        var other = Input("other", cellular);
        var copying = new Constructor("Cell", cellular, [other.Parameter!], Public)
        {
            DeclaringType = cellular,
            Body = HandBuiltModel.Block(new ExpressionStatement(Assign(Access(Ref(other), x), Literal(integer, 99L)))),
        };
        cellular.ConstructorsList.Add(copying);
        var holder = HandBuiltModel.Class("Holder");
        var c = Field(holder, "c", cellular);
        var d = new LocalVariable("d", cellular);
        var poke = Method(holder, "Poke", integer, Public with { IsReadonly = true }, HandBuiltModel.Block(
            new LocalDeclarationStatement(d, new NewExpression(Node(37), cellular, [new FieldReferenceExpression(Node(), c)], copying, [], [cellular])),
            new ReturnStatement(Literal(integer, 0L), integer)));
        return Judge(new ReadonlyAnalyzer(), poke);
    }

    private IReadOnlyList<Diagnostic> ReadonlyMemberInterpolatingAMistIFormattable()
    {
        var loud = new ClassType("Loud", "Probe", new Scope(null), null, DeclarationFragments.At<BodyTypeTypeDeclarationNode>(40));
        loud.BaseAbstractTypes.Add(new AbstractType("IFormattable", "System", new Scope(null), null, null));
        var hits = Field(loud, "hits", integer);
        _ = Method(loud, "ToString", text, Public, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new FieldReferenceExpression(Node(), hits), Literal(integer, 1L))),
            new ReturnStatement(Literal(text, "loud"), text)), Input("format", text), Input("provider", text));
        var viewer = HandBuiltModel.Class("Viewer");
        var l = Field(viewer, "l", loud);
        var interpolated = new InterpolatedStringExpression(Node(), [new InterpolatedStringHole(new FieldReferenceExpression(Node(37), l))], text);
        return Judge(new ReadonlyAnalyzer(), Method(viewer, "Show", text, Public with { IsReadonly = true }, Returning(interpolated)));
    }

    private IReadOnlyList<Diagnostic> ReadonlyMemberReadingANoisyStatic()
    {
        var tally = HandBuiltModel.Class("Tally");
        var bump = Method(tally, "Bump", integer, Static, HandBuiltModel.Block(WriteStatic(ticks), new ReturnStatement(Literal(integer, 1L), integer)));
        var noisy = HandBuiltModel.Class("Noisy");
        var seeded = Field(noisy, "X", integer, isStatic: true, isReadonly: true);
        seeded.Initializer = Call(bump, null, 0);
        var viewer = HandBuiltModel.Class("Viewer");
        return Judge(new ReadonlyAnalyzer(), Method(viewer, "Look", integer, Public with { IsReadonly = true }, Returning(StaticRead(seeded, 37))));
    }

    private IReadOnlyList<Diagnostic> ReadonlyMemberCallingAFunctionValue(bool pure)
    {
        var runner = HandBuiltModel.Class("Runner");
        var f = Input("f", Function(pure, integer));
        return Judge(new ReadonlyAnalyzer(), Method(runner, "Run", integer, Public with { IsReadonly = true },
            Returning(new InvocationExpression(Node(37), Ref(f), [], integer, null, [])), f));
    }

    private IReadOnlyList<Diagnostic> StructMemberAssigningThis(MemberModifiers modifiers, IProgramAnalyzer analyzer)
    {
        var counter = HandBuiltModel.Struct("S");
        _ = Field(counter, "V", integer);
        var next = Method(counter, "Next", unit, modifiers, HandBuiltModel.Block(
            new ExpressionStatement(Assign(new ThisExpression(Node(37), counter), Fresh(counter)))));
        return Judge(analyzer, next);
    }
}