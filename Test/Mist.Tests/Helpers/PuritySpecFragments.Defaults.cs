using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed partial class PuritySpecFragments
{
    private IReadOnlyList<Diagnostic>? Defaults(string name)
    {
        return name switch
        {
            "initializer-allocating-through-a-pure-constructor-of-a-pure-type" => Initialized(Box(), "V", Vec()),
            "initializer-allocating-through-a-pure-constructor" => Initialized(Box(), "B", Bag()),
            "initializer-allocating-through-an-inferred-pure-constructor" => Initialized(Box(), "Q", Quiet()),
            "initializer-reading-a-readonly-static" => Initialized(Box(), "N", StaticRead(limit, 37)),
            "initializer-adding-two-literals" => Initialized(Box(), "N", Arithmetic(Number(1), BinaryOperatorKind.Add, Number(2))),
            "initializer-of-an-array-literal" => Initialized(Box(), "Xs", new ArrayLiteralExpression(Node(37), integer, [Number(1), Number(2)], ArrayOfInt())),
            "struct-initializer-reading-a-readonly-static" => Initialized(Box(isStruct: true), "N", StaticRead(limit, 37)),
            "default-entry-reading-a-readonly-static" => Defaulted("N", StaticRead(limit, 37)),
            "default-entry-allocating-through-a-pure-constructor" => Defaulted("V", Vec()),
            "initializer-reading-a-mutable-static" => Initialized(Box(), "N", StaticRead(counter, 37)),
            "initializer-allocating-through-an-impure-constructor" => Initialized(Box(), "L", Loud()),
            "struct-initializer-reading-a-mutable-static" => Initialized(Box(isStruct: true), "N", StaticRead(counter, 37)),
            "property-initializer-reading-a-mutable-static" => PropertyInitialized(StaticRead(counter, 37)),
            "default-entry-reading-a-mutable-static" => Defaulted("N", StaticRead(counter, 37)),
            "default-entry-allocating-through-an-impure-constructor" => Defaulted("L", Loud()),
            "initializer-of-a-type-with-only-an-argument-taking-constructor" => Initialized(Box(), "N", StaticRead(counter, 37), constructed: true),
            "impure-allocation-in-an-initializer-of-a-type-with-only-an-argument-taking-constructor" => Initialized(Box(), "L", Loud(), constructed: true),
            "struct-initializer-of-a-type-with-only-an-argument-taking-constructor" => Initialized(Box(isStruct: true), "N", StaticRead(counter, 37), constructed: true),
            "static-initializer-reading-a-mutable-static" => Initialized(Box(), "N", StaticRead(counter, 37), isStatic: true),
            _ => null,
        };
    }

    private IReadOnlyList<Diagnostic> Initialized(ConcreteType box, string name, Expression initializer, bool isStatic = false, bool constructed = false)
    {
        var field = Field(box, name, initializer.Type, isStatic: isStatic);
        field.Initializer = initializer;

        if(constructed)
        {
            _ = Constructor(box, Public, HandBuiltModel.Block(), Input("n", integer));
        }

        return model.Analyze(new PurityAnalyzer(), types: [box]);
    }

    private IReadOnlyList<Diagnostic> Defaulted(string name, Expression value)
    {
        var box = Box();
        _ = Field(box, name, value.Type);
        _ = Constructor(box, Public, HandBuiltModel.Block(), Input("n", integer));
        box.DefaultBlock = new DefaultBlock([new DefaultValueEntry(name, value)], new DefaultBlockDeclarationNode());
        return model.Analyze(new PurityAnalyzer(), types: [box]);
    }

    private IReadOnlyList<Diagnostic> PropertyInitialized(Expression initializer)
    {
        var box = Box();
        var property = AutoProperty(box, "N", integer, settable: true);
        property.Initializer = initializer;
        return model.Analyze(new PurityAnalyzer(), types: [box]);
    }

    private NewExpression Vec()
    {
        var vec = HandBuiltModel.Class("Vec");
        vec.Modifiers = Pure;
        var x = Field(vec, "X", integer, isReadonly: true);
        var seed = Input("x", integer);
        var constructor = Constructor(vec, Pure, HandBuiltModel.Block(Set(Own(x), Ref(seed))), seed);
        return Alloc(vec, constructor, 37, Number(7));
    }

    private NewExpression Bag()
    {
        var bag = HandBuiltModel.Class("Bag");
        _ = Field(bag, "Count", integer);
        return Alloc(bag, Constructor(bag, Pure, HandBuiltModel.Block()), 37);
    }

    private NewExpression Quiet()
    {
        var quiet = HandBuiltModel.Class("Quiet");
        var v = Field(quiet, "V", integer);
        var seed = Input("v", integer);
        var constructor = Constructor(quiet, Public, HandBuiltModel.Block(Set(Own(v), Ref(seed))), seed);
        return Alloc(quiet, constructor, 37, Number(7));
    }

    private NewExpression Loud()
    {
        var loud = HandBuiltModel.Class("Loud");
        _ = Field(loud, "V", integer);
        return Alloc(loud, Constructor(loud, Public, HandBuiltModel.Block(Set(StaticRead(counter), Number(1)))), 37);
    }

    private static ConcreteType Box(bool isStruct = false)
    {
        return isStruct ? HandBuiltModel.Struct("Box") : HandBuiltModel.Class("Box");
    }
}