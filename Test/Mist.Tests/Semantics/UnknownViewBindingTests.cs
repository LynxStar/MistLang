// Tier 2 (Functional): what the binder resolves on a receiver closed with `unknown` (unknown-type
// Requirement 10), over hand-built generic types and the lookup, selection and direction chain the
// postfix, index and assignment binders run. Members here have no bodies, so their classification is
// their signature's; one body-based case marks its member complete, which is how the real binder
// hands the summaries a tree it has already built.

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class UnknownViewBindingTests
{
    private const string Hidden = "MIST0395";

    private readonly Fixture f = new();

    [Fact]
    public void AWriterIsHiddenAndNamedWithItsParameterAndReceiver()
    {
        var bound = f.Bind("sealedBox", MethodCall("Set", Reference("one")));

        var diagnostic = Assert.Single(f.Errors());
        Assert.Equal(Hidden, diagnostic.Number.Value);
        Assert.Equal(
            "'Set' cannot be called on 'Sealed<unknown>': it would write to storage typed by 'T', which 'Sealed<unknown>' closes with 'unknown'. Narrow the receiver with an 'is' pattern to a concrete construction first.",
            diagnostic.Message);
        _ = Assert.IsType<ErrorType>(bound.Type);
    }

    /// <summary>
    /// Hiding the writer removes it from the candidates rather than refusing the name: the reader
    /// overload of the same name is what the call selects, and it is selected on its own terms.
    /// </summary>
    [Fact]
    public void AReaderOverloadIsSelectedWhereTheWriterIsHidden()
    {
        var bound = f.Bind("widened", MethodCall("Set", Reference("text")));

        Assert.Empty(f.Errors());
        var call = Assert.IsType<InvocationExpression>(bound);
        Assert.Same(f.String, call.Method?.ParametersList[0].Type);
    }

    [Fact]
    public void AReaderAndAConcreteReceiverAreUntouched()
    {
        var size = Assert.IsType<InvocationExpression>(f.Bind("widened", MethodCall("Size")));
        var concrete = Assert.IsType<InvocationExpression>(f.Bind("ints", MethodCall("Set", Reference("one"))));

        Assert.Empty(f.Errors());
        Assert.Same(f.Int32, size.Type);
        Assert.Same(f.Box.TypeParametersList[0], concrete.Method?.ParametersList[0].Type);
    }

    [Fact]
    public void AFieldReadsAsUnknownAndCannotBeAssigned()
    {
        var read = f.Bind("widened", Access("Value"));
        var written = f.Assign("widened", Access("Value"), Reference("one"));
        var count = f.Assign("widened", Access("Count"), Reference("one"));

        _ = Assert.IsType<UnknownType>(Assert.IsType<MemberAccessExpression>(read).Type);
        _ = Assert.IsType<AssignmentExpression>(count);
        Assert.True(Assert.IsType<MemberAccessExpression>(Assert.IsType<AssignmentExpression>(written).Target).IsFailed);
        var diagnostic = Assert.Single(f.Errors());
        Assert.Contains("'Value' cannot be assigned on 'Box<unknown>'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void APropertyAndAnIndexerAreJudgedPerDirection()
    {
        _ = f.Bind("widened", Access("Item"));
        _ = f.Bind("widened", IndexWith(Reference("one")));
        Assert.Empty(f.Errors());

        _ = f.Assign("widened", Access("Item"), Reference("one"));
        _ = f.Assign("widened", IndexWith(Reference("one")), Reference("one"));

        Assert.Equal(
            ["'Item' cannot be assigned on 'Box<unknown>'", "'this[]' cannot be assigned on 'Box<unknown>'"],
            f.Errors().Select(diagnostic => diagnostic.Message[..diagnostic.Message.IndexOf(':', StringComparison.Ordinal)]));
    }

    [Fact]
    public void OnlyTheParameterBoundToUnknownHidesMembers()
    {
        var first = f.Bind("pair", MethodCall("SetFirst", Reference("one")));
        _ = f.Bind("pair", MethodCall("SetSecond", Reference("text")));

        _ = Assert.IsType<InvocationExpression>(first);
        var diagnostic = Assert.Single(f.Errors());
        Assert.StartsWith("'SetSecond' cannot be called on 'Pair<", diagnostic.Message, StringComparison.Ordinal);
        Assert.Contains("typed by 'B'", diagnostic.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AnInheritedWriterIsHiddenThroughTheBaseClauseAndANestedArgumentCounts()
    {
        _ = f.Bind("derived", MethodCall("Keep", Reference("one")));
        _ = f.Bind("nested", MethodCall("Set", Reference("one")));

        Assert.Equal(
            ["'Keep' cannot be called on 'Derived<unknown>'", "'Set' cannot be called on 'Sealed<Box<unknown>>'"],
            f.Errors().Select(diagnostic => diagnostic.Message[..diagnostic.Message.IndexOf(':', StringComparison.Ordinal)]));
        Assert.Contains("typed by 'X'", f.Errors().First().Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A member the binder has finished is read as it stands: Forward's tree calls Set, and the
    /// summaries read that tree rather than binding anything, so the call inherits Set's classification.
    /// </summary>
    [Fact]
    public void ACompletedBodyClassifiesThroughItsCalls()
    {
        f.DeclareForward();

        _ = f.Bind("widened", MethodCall("Forward", Reference("one")));
        var echo = f.Bind("widened", MethodCall("Echo", Reference("one")));

        var diagnostic = Assert.Single(f.Errors());
        Assert.Contains("'Forward' cannot be called on 'Box<unknown>'", diagnostic.Message, StringComparison.Ordinal);
        _ = Assert.IsType<InvocationExpression>(echo);
    }

    private static ExpressionNode Reference(string local)
    {
        return BindingFragments.Expression(BindingFragments.Identifier(local));
    }

    private static MethodCallSuffixPostfixSuffixNode MethodCall(string name, params ExpressionNode[] arguments)
    {
        var suffix = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>();
        suffix.Name = BindingFragments.Token(name);
        suffix.Arguments = BindingFragments.Arguments(arguments);
        return suffix;
    }

    private static MemberAccessSuffixPostfixSuffixNode Access(string name)
    {
        var suffix = BindingFragments.At<MemberAccessSuffixPostfixSuffixNode>();
        suffix.Name = BindingFragments.Token(name);
        return suffix;
    }

    private static IndexSuffixPostfixSuffixNode IndexWith(ExpressionNode index)
    {
        var suffix = BindingFragments.At<IndexSuffixPostfixSuffixNode>();
        suffix.Index = index;
        return suffix;
    }

    private sealed class Fixture
    {
        public BindingFragments Fragments { get; } = new();

        public ClassType Int32 => Fragments.Built.Type("Int32");

        public ClassType String => Fragments.Built.Type("String");

        public ClassType Box { get; }

        private Method SetT { get; }

        public Fixture()
        {
            Box = Declare("Box<>", "T");
            var t = Box.TypeParametersList[0];
            Field(Box, "Value", t);
            Field(Box, "Count", Int32);
            SetT = Member(Box, "Set", Int32, [new Parameter("value", t)]);
            _ = Member(Box, "Set", Int32, [new Parameter("label", String)]);
            _ = Member(Box, "Size", Int32, []);
            Box.PropertiesList.Add(new Property("Item", t, DeclarationFragments.Public)
            {
                DeclaringType = Box,
                Getter = new PropertyAccessor { IsAuto = true },
                Setter = new PropertyAccessor { IsAuto = true },
            });
            Box.IndexersList.Add(new Indexer(t, [new Parameter("index", Int32)], DeclarationFragments.Public)
            {
                DeclaringType = Box,
                Getter = new PropertyAccessor { IsAuto = true },
                Setter = new PropertyAccessor { IsAuto = true },
            });

            var sealedBox = Declare("Sealed<>", "T");
            _ = Member(sealedBox, "Set", Int32, [new Parameter("value", sealedBox.TypeParametersList[0])]);

            var pair = Declare("Pair<,>", "A", "B");
            _ = Member(pair, "SetFirst", Int32, [new Parameter("a", pair.TypeParametersList[0])]);
            _ = Member(pair, "SetSecond", Int32, [new Parameter("b", pair.TypeParametersList[1])]);

            var baseType = Declare("Base<>", "X");
            _ = Member(baseType, "Keep", Int32, [new Parameter("value", baseType.TypeParametersList[0])]);
            var derived = Declare("Derived<>", "Y");
            derived.BaseType = Construct(baseType, derived.TypeParametersList[0]);

            _ = Fragments.Local("widened", Construct(Box, UnknownType.Instance));
            _ = Fragments.Local("ints", Construct(Box, Int32));
            _ = Fragments.Local("nested", Construct(sealedBox, Construct(Box, UnknownType.Instance)));
            _ = Fragments.Local("sealedBox", Construct(sealedBox, UnknownType.Instance));
            _ = Fragments.Local("pair", Construct(pair, Int32, UnknownType.Instance));
            _ = Fragments.Local("derived", Construct(derived, UnknownType.Instance));
            _ = Fragments.Local("one", Int32);
            _ = Fragments.Local("text", String);
        }

        public IEnumerable<Diagnostic> Errors()
        {
            return Fragments.Diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error);
        }

        public Expression Bind(string local, PostfixSuffixNode suffix)
        {
            return Fragments.Bind(BindingFragments.Identifier(local), suffix);
        }

        public Expression Assign(string local, PostfixSuffixNode suffix, ExpressionNode value)
        {
            var assignment = BindingFragments.Expression(BindingFragments.Identifier(local), suffix).AsAssignmentOrNull()!;
            assignment.Group0 = new AssignmentNodeGroup0 { Operator = BindingFragments.Token("="), Value = value };

            return ExpressionBinder.BindExpression(new AssignmentFormExpressionNode { Assignment = assignment }, Fragments.Binding, Fragments.Builder);
        }

        /// <summary>
        /// Forward's body calls Set; Echo's body calls Size. Both are complete, so the summaries read
        /// these trees; the type joins the compiled assembly so the summaries look at it at all.
        /// </summary>
        public void DeclareForward()
        {
            Box.Assembly = Fragments.Built.Model.AddSelfAssembly("Probe");
            var t = Box.TypeParametersList[0];
            var node = HandBuiltModel.Node();
            var value = new LocalVariable("value", t);
            var size = Box.MethodsList.Single(method => method.Name == "Size");
            var forward = Member(Box, "Forward", Int32, [new Parameter("value", t)], HandBuiltModel.Block(
                new ExpressionStatement(new InvocationExpression(
                    node, new MethodReferenceExpression(node, SetT), [new LocalReferenceExpression(node, value)], Int32, SetT, [t]))));
            var echo = Member(Box, "Echo", Int32, [new Parameter("value", t)], HandBuiltModel.Block(
                new ExpressionStatement(new InvocationExpression(node, new MethodReferenceExpression(node, size), [], Int32, size, []))));
            Fragments.Builder.StorageWrites.MarkComplete(forward);
            Fragments.Builder.StorageWrites.MarkComplete(echo);
        }

        private ClassType Declare(string name, params string[] parameterNames)
        {
            var type = UnknownViewDecisionTests.Generic(name, null, parameterNames);
            Fragments.Register(type);
            return type;
        }

        private ConstructedType Construct(ClassType definition, params IMistType[] arguments)
        {
            return Fragments.Built.Interner.GetOrCreateConstructedType(definition, arguments);
        }

        private static void Field(ConcreteType owner, string name, IMistType type)
        {
            owner.FieldsList.Add(new Field(name, type, OwnershipKind.Unrestricted, DeclarationFragments.Public) { DeclaringType = owner });
        }

        private static Method Member(MemberType owner, string name, IMistType returns, List<Parameter> parameters, Block? body = null)
        {
            var method = new Method(name, returns, parameters, DeclarationFragments.Public)
            {
                DeclaringType = owner,
                Scope = new MethodScope(owner.Scope),
                Body = body,
            };
            owner.MethodsList.Add(method);
            return method;
        }
    }
}