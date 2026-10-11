using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.Helpers;

internal sealed class FormattingFragments
{
    private static readonly MemberModifiers Public = new() { Accessibility = Accessibility.Public };
    private static readonly MemberModifiers Readonly = Public with { IsReadonly = true };

    private readonly AbstractType formattable = new("IFormattable", "System", new Scope(null), null, null);
    private readonly List<MistType> types = [];

    public FormattingFragments()
    {
        Text = Model.Type("String");
        Text.Modifiers = new MemberModifiers { IsReadonly = true, IsIntrinsic = true };
        Integer = Model.Type("Int32");
        Integer.Modifiers = new MemberModifiers { IsReadonly = true, IsIntrinsic = true };
        Void = Model.Type("Void");
        Program = Declare(HandBuiltModel.Class("Program"));
        Box = Formatter("Box", mutates: true);
        Calm = Formatter("Calm", mutates: false, declaredReadonly: true);
        Quiet = Formatter("Quiet", mutates: false);
        Wrapper = GenericFormatter("Wrapper", declaredReadonly: false);
        SealedWrapper = GenericFormatter("SealedWrapper", declaredReadonly: true);
    }

    public HandBuiltModel Model { get; } = new();

    public ClassType Text { get; }

    public ClassType Integer { get; }

    public ClassType Void { get; }

    public ClassType Program { get; }

    public ClassType Box { get; }

    public ClassType Calm { get; }

    public ClassType Quiet { get; }

    public ClassType Wrapper { get; }

    public ClassType SealedWrapper { get; }

    public ConstructedType Constructed(ClassType definition, IMistType argument)
    {
        return Model.Interner.GetOrCreateConstructedType(definition, [argument]);
    }

    public Method Formats(string name, bool isReadonly, bool concatenates = false)
    {
        var parameter = TypeParameterNamed("T");
        var item = Item(parameter);

        return Generic(name, isReadonly, parameter, Returning(concatenates ? Concatenate(HandBuiltModel.Literal(Text, "c"), item) : Interpolate(item)));
    }

    public Method FormatsOptional(string name, bool isReadonly)
    {
        var parameter = TypeParameterNamed("T");
        var optional = new UnionType([parameter, Void]);

        return Generic(name, isReadonly, parameter, Returning(Interpolate(Value(optional))));
    }

    public Method Forwards(string name, bool isReadonly, Method callee, int offset)
    {
        var parameter = TypeParameterNamed("U");

        return Generic(name, isReadonly, parameter, Returning(Call(callee, parameter, Item(parameter), offset)));
    }

    public Method Calls(string name, bool isReadonly, Method callee, IMistType argument, int offset)
    {
        return Member(name, isReadonly, Returning(Call(callee, argument, Value(argument), offset)));
    }

    public Method Interpolates(string name, IMistType formatted, bool isReadonly = true)
    {
        return Member(name, isReadonly, Returning(Interpolate(Value(formatted))));
    }

    public (ClassType Keep, Method Describe) ReadonlyGeneric(string name)
    {
        var keep = Declare(HandBuiltModel.Class(name));
        keep.Modifiers = Readonly;
        var parameter = new TypeParameter("K", 0, keep.Scope, keep);
        keep.TypeParametersList.Add(parameter);
        var item = new Field("item", parameter, default, Readonly) { DeclaringType = keep };
        keep.FieldsList.Add(item);
        var describe = new Method("Describe", Text, [], Readonly)
        {
            DeclaringType = keep,
            Body = Returning(Interpolate(new FieldReferenceExpression(HandBuiltModel.Node(), item))),
        };
        keep.MethodsList.Add(describe);
        return (keep, describe);
    }

    public (ClassType Base, Method Show) GenericBase(string name)
    {
        var type = Declare(HandBuiltModel.Class(name));
        var parameter = new TypeParameter("T", 0, type.Scope, type);
        type.TypeParametersList.Add(parameter);
        var show = new Method("Show", Text, [], Public) { DeclaringType = type, Body = Returning(Interpolate(Item(parameter))) };
        type.MethodsList.Add(show);
        return (type, show);
    }

    public (ClassType Shelf, Indexer Slot) GenericIndexed(string name)
    {
        var type = Declare(HandBuiltModel.Class(name));
        var parameter = new TypeParameter("T", 0, type.Scope, type);
        type.TypeParametersList.Add(parameter);
        var held = new Field("held", parameter, default, default) { DeclaringType = type };
        type.FieldsList.Add(held);
        var slot = new Indexer(Text, [new Parameter("index", Integer)], Public)
        {
            Getter = new PropertyAccessor { Body = Returning(Interpolate(new FieldReferenceExpression(HandBuiltModel.Node(), held))) },
            DeclaringType = type,
        };
        type.IndexersList.Add(slot);
        return (type, slot);
    }

    public Method Indexes(string name, Indexer slot, IMistType receiver, int offset)
    {
        var read = new IndexExpression(HandBuiltModel.Node(offset), Value(receiver), HandBuiltModel.Literal(Integer, 0L), Text) { Indexer = slot };
        return Member(name, isReadonly: true, Returning(read));
    }

    public ClassType Derived(string name, ClassType baseDefinition, IMistType argument)
    {
        return Declare(HandBuiltModel.Class(name, Constructed(baseDefinition, argument)));
    }

    public Method CallsThrough(string name, Method callee, IMistType receiver, int offset)
    {
        var node = HandBuiltModel.Node(offset);
        var call = new InvocationExpression(node, new MemberAccessExpression(node, Value(receiver), callee.Name, Text), [], Text, callee, []);
        return Member(name, isReadonly: true, Returning(call));
    }

    public (Method Declared, Method Closed) ReadonlyExtension(IMistType closedWith)
    {
        var block = new ExtensionBlock("self", "Probe", new Scope(null), null);
        var parameter = new TypeParameter("T", 0, block.Scope, null);
        block.TypeParametersList.Add(parameter);
        var declared = new Method("Show", Text, [], Readonly)
        {
            DeclaringBlock = block,
            Body = Returning(Interpolate(Value(parameter))),
        };

        return (declared, ExtensionSpecialization.Close(declared, block, [closedWith], Model.Interner));
    }

    public Method CallsClosed(string name, Method closed, int offset)
    {
        var node = HandBuiltModel.Node(offset);
        var call = new InvocationExpression(node, new MethodReferenceExpression(node, closed, []), [], Text, closed, []);
        return Member(name, isReadonly: false, Returning(call));
    }

    public InvocationExpression Call(Method callee, IMistType typeArgument, Expression argument, int offset)
    {
        var node = HandBuiltModel.Node(offset);
        var closing = TypeSubstitution.ForParameters(callee.TypeParametersList, [typeArgument]);
        Model.Model.ConstructorConstraintClosures.RecordMethod(callee, [typeArgument], closing, node);

        return new InvocationExpression(node, new MethodReferenceExpression(node, callee, [typeArgument]), [argument], Text, callee, [typeArgument]);
    }

    public void Close(MistType definition, IMistType argument, int offset)
    {
        Model.Model.ConstructorConstraintClosures.RecordType(definition, [argument], HandBuiltModel.Node(offset));
    }

    public IReadOnlyList<Diagnostic> Analyze(params Method[] members)
    {
        var analyzer = new ReadonlyAnalyzer();
        var diagnostics = new DiagnosticBag();
        var roots = members.Select(member => new SourceRoot(member.Body!, member.DeclaringType, MistAssembly.Synthetic, member)).ToList();
        var nodes = new List<object>();
        var owners = new Dictionary<object, SourceRoot>(ReferenceEqualityComparer.Instance);

        foreach(var root in roots)
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(root.Root))
            {
                nodes.Add(node);
                owners[node] = root;
            }
        }

        analyzer.Analyze(new AnalysisContext(Model.Model, new AnalysisScope(types, roots, [], []), nodes, owners,
            new Dictionary<object, AssignmentExpression>(ReferenceEqualityComparer.Instance),
            new HashSet<string>(StringComparer.Ordinal) { "hand-built.mist" }, diagnostics, analyzer.Id, CloneConversionRefusals.None));
        return diagnostics.ToImmutableArray();
    }

    public InterpolatedStringExpression Interpolate(Expression hole)
    {
        return new InterpolatedStringExpression(HandBuiltModel.Node(), [new InterpolatedStringHole(hole)], Text);
    }

    public static LocalReferenceExpression Value(IMistType type)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("value", type) { Parameter = new Parameter("value", type) });
    }

    private ClassType Formatter(string name, bool mutates, bool declaredReadonly = false)
    {
        var type = Declare(HandBuiltModel.Class(name));
        type.BaseAbstractTypes.Add(formattable);
        var hits = new Field("hits", Integer, default, default) { DeclaringType = type };
        type.FieldsList.Add(hits);
        var body = mutates
            ? HandBuiltModel.Block(
                new ExpressionStatement(new AssignmentExpression(
                    HandBuiltModel.Node(), new FieldReferenceExpression(HandBuiltModel.Node(), hits), HandBuiltModel.Literal(Integer, 1L), Integer)),
                new ReturnStatement(HandBuiltModel.Literal(Text, name), Text))
            : Returning(HandBuiltModel.Literal(Text, name));
        AddToString(type, body, declaredReadonly);
        return type;
    }

    private ClassType GenericFormatter(string name, bool declaredReadonly)
    {
        var type = Declare(HandBuiltModel.Class(name));
        type.BaseAbstractTypes.Add(formattable);
        var parameter = new TypeParameter("W", 0, type.Scope, type);
        type.TypeParametersList.Add(parameter);
        var item = new Field("Item", parameter, default, default) { DeclaringType = type };
        type.FieldsList.Add(item);
        AddToString(type, Returning(Interpolate(new FieldReferenceExpression(HandBuiltModel.Node(), item))), declaredReadonly);
        return type;
    }

    private void AddToString(ClassType type, Block body, bool declaredReadonly)
    {
        type.MethodsList.Add(new Method("ToString", Text, [new Parameter("format", Text), new Parameter("provider", Text)], declaredReadonly ? Readonly : Public)
        {
            DeclaringType = type,
            Body = body,
        });
    }

    private Method Generic(string name, bool isReadonly, TypeParameter parameter, Block body)
    {
        var method = Member(name, isReadonly, body);
        method.TypeParametersList.Add(parameter);
        return method;
    }

    private Method Member(string name, bool isReadonly, Block body)
    {
        var method = new Method(name, Text, [], (isReadonly ? Readonly : Public) with { IsStatic = true }) { DeclaringType = Program, Body = body };
        Program.MethodsList.Add(method);
        return method;
    }

    private TypeParameter TypeParameterNamed(string name)
    {
        return new TypeParameter(name, 0, Program.Scope, null);
    }

    private static LocalReferenceExpression Item(TypeParameter parameter)
    {
        return new LocalReferenceExpression(HandBuiltModel.Node(), new LocalVariable("item", parameter) { Parameter = new Parameter("item", parameter) });
    }

    private BinaryExpression Concatenate(Expression left, Expression right)
    {
        return new BinaryExpression(HandBuiltModel.Node(), left, BinaryOperatorKind.Add, right, Text);
    }

    private static Block Returning(Expression expression)
    {
        return HandBuiltModel.Block(new ReturnStatement(expression, expression.Type));
    }

    private T Declare<T>(T type) where T : MistType
    {
        types.Add(type);
        return type;
    }
}