using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

/// <summary>
/// What a call on a value binds to, over hand-built declared and CLR-shaped types. Asserted on the
/// model because emission writes the call as source spelled it and lets Roslyn resolve it again, so a
/// program that runs says nothing about whether the binder judged the callee.
/// </summary>
[Trait("Tier", "2")]
public sealed class CallabilityTests
{
    private const string NotCallable = "MIST_PM_NOT_CALLABLE";

    [Theory]
    [InlineData("holder.Number(one)", "'Number' is not a method, and its type 'int' is not a function type, so it cannot be called.")]
    [InlineData("Holder.Shared(one)", "'Shared' is not a method, and its type 'int' is not a function type, so it cannot be called.")]
    [InlineData("count(one)", "'count' is not a method, and its type 'int' is not a function type, so it cannot be called.")]
    [InlineData("holder.GetNumber()(one)", "A value of type 'int' is not a function, so it cannot be called.")]
    [InlineData("maybe?.Number(one)", "'Number' is not a method, and its type 'int' is not a function type, so it cannot be called.")]
    public void AValueWhoseTypeIsNotAFunctionIsNotCalled(string call, string message)
    {
        var f = new Fixture();

        _ = f.Bind(call);

        var diagnostic = Assert.Single(f.Errors());
        Assert.Equal((NotCallable, message), (diagnostic.Id.Value, diagnostic.Message));
    }

    [Theory]
    [InlineData("field", "Number", "'Number'")]
    [InlineData("property", "Size", "'Size'")]
    [InlineData("parameter", "count", "'count'")]
    public void AMemberOrParameterNamedBareIsJudgedTheSameWay(string kind, string name, string named)
    {
        var f = new Fixture();

        _ = f.BindBare(kind, name, f.Int32);

        var diagnostic = Assert.Single(f.Errors());
        Assert.Equal(NotCallable, diagnostic.Id.Value);
        Assert.Contains(named, diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("holder.Handler(one)")]
    [InlineData("holder.Transform(one)")]
    [InlineData("Holder.SharedHandler(one)")]
    [InlineData("handler(one)")]
    [InlineData("Holder.CallsItsParameter(one, x => x)")]
    [InlineData("holder.CallsItsOwnHandlers()")]
    public void AFunctionTypedValueIsCalledForItsResult(string call)
    {
        var f = new Fixture();

        var bound = f.Bind(call);

        Assert.Empty(f.Errors());
        Assert.Same(f.Int32, Assert.IsType<InvocationExpression>(bound).Type);
    }

    [Theory]
    [InlineData("field", "Handler")]
    [InlineData("property", "Transform")]
    public void ABareFunctionTypedMemberIsCalledForItsResult(string kind, string name)
    {
        var f = new Fixture();

        var bound = f.BindBare(kind, name, f.IntToInt);

        Assert.Empty(f.Errors());
        Assert.Same(f.Int32, Assert.IsType<InvocationExpression>(bound).Type);
    }

    [Theory]
    [InlineData("optional(one)")]
    [InlineData("maybe?.Handler(one)")]
    public void AnOptionalCallableIsLeftToTheRuleAboutOptionals(string call)
    {
        var f = new Fixture();

        _ = f.Bind(call);

        Assert.Empty(f.Errors());
    }

    [Fact]
    public void AnExtensionMethodAnswersTheCallAnUncallableMemberCannot()
    {
        var f = new Fixture();

        var call = Assert.IsType<InvocationExpression>(f.Bind("holder.Label(one)"));

        Assert.Empty(f.Errors());
        Assert.Equal(("Label", 1), (call.Method?.Name, call.Method?.ParametersList.Count));
        Assert.Same(f.String, call.Type);
    }

    [Fact]
    public void AClrPropertyIsNotCalledAndAnUnimportedExtensionIsNamed()
    {
        var f = new Fixture();
        f.DeclareClrShapes(importLinq: false);

        _ = f.Bind("numbers.Count()");
        _ = f.Bind("builder.Length(one)");

        Assert.Equal(
            [
                "'Count' is not a method, and its type 'int' is not a function type, so it cannot be called. An extension method named 'Count' is declared in 'System.Linq', which is not imported here.",
                "'Length' is not a method, and its type 'int' is not a function type, so it cannot be called.",
            ],
            f.Fragments.Diagnostics.ToImmutableArray().Where(d => d.Id.Value == NotCallable).Select(d => d.Message));
    }

    [Fact]
    public void AnImportedClrExtensionAnswersTheCallAClrPropertyCannot()
    {
        var f = new Fixture();
        f.DeclareClrShapes(importLinq: true);

        var call = Assert.IsType<InvocationExpression>(f.Bind("numbers.Count()"));

        Assert.Empty(f.Errors());
        Assert.Equal("Count", call.Method?.Name);
        Assert.Same(f.Int32, call.Type);
    }

    [Theory]
    [InlineData("positive(one)")]
    [InlineData("clrHolder.Callback(one)")]
    public void AClrDelegateIsCalled(string call)
    {
        var f = new Fixture();
        f.DeclareClrShapes(importLinq: false);

        _ = f.Bind(call);

        Assert.Empty(f.Errors());
    }

    private sealed class Fixture
    {
        public BindingFragments Fragments { get; } = new();

        public ClassType Int32 => Fragments.Built.Type("Int32");

        public ClassType String => Fragments.Built.Type("String");

        public FunctionType IntToInt { get; }

        private ClassType Holder { get; }

        public Fixture()
        {
            IntToInt = Fragments.Built.Interner.GetOrCreateFunctionType([Int32], Int32);
            Holder = HandBuiltModel.Class("Holder");
            Fragments.Register(Holder);
            Field(Holder, "Number", Int32);
            Field(Holder, "Shared", Int32, isStatic: true);
            Field(Holder, "Handler", IntToInt);
            Field(Holder, "SharedHandler", IntToInt, isStatic: true);
            Property(Holder, "Label", String);
            Property(Holder, "Transform", IntToInt);
            _ = BindingFragments.Member(Holder, "GetNumber", Int32);
            _ = BindingFragments.Member(Holder, "CallsItsOwnHandlers", Int32);
            Holder.MethodsList.Add(new Method(
                "CallsItsParameter", Int32, [new Parameter("count", Int32), new Parameter("handler", IntToInt)],
                DeclarationFragments.Public with { IsStatic = true })
            {
                DeclaringType = Holder,
                Scope = Holder.Scope,
            });
            Extension("Probe", Holder, imported: true, block => new Method("Label", String, [new Parameter("repeat", Int32)], DeclarationFragments.Public)
            {
                Scope = new MethodScope(block.Scope),
            });
            _ = Fragments.Local("holder", Holder);
            _ = Fragments.Local("one", Int32);
            _ = Fragments.Local("count", Int32);
            _ = Fragments.Local("handler", IntToInt);
            _ = Fragments.Local("optional", Optional(IntToInt));
            _ = Fragments.Local("maybe", Optional(Holder));
        }

        public IEnumerable<Diagnostic> Errors()
        {
            return Fragments.Diagnostics.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error);
        }

        public Expression Bind(string call)
        {
            var one = Reference("one");
            return call switch
            {
                "holder.Number(one)" => Fragments.Bind(BindingFragments.Identifier("holder"), MethodCall("Number", one)),
                "Holder.Shared(one)" => Fragments.Bind(BindingFragments.Identifier("Holder"), MethodCall("Shared", one)),
                "count(one)" => Fragments.Bind(BindingFragments.Identifier("count"), Invocation(one)),
                "holder.GetNumber()(one)" => Fragments.Bind(BindingFragments.Identifier("holder"), MethodCall("GetNumber"), Invocation(one)),
                "maybe?.Number(one)" => Fragments.Bind(BindingFragments.Identifier("maybe"), OptionalMethodCall("Number", one)),
                "holder.Handler(one)" => Fragments.Bind(BindingFragments.Identifier("holder"), MethodCall("Handler", one)),
                "holder.Transform(one)" => Fragments.Bind(BindingFragments.Identifier("holder"), MethodCall("Transform", one)),
                "Holder.SharedHandler(one)" => Fragments.Bind(BindingFragments.Identifier("Holder"), MethodCall("SharedHandler", one)),
                "handler(one)" => Fragments.Bind(BindingFragments.Identifier("handler"), Invocation(one)),
                "Holder.CallsItsParameter(one, x => x)" => Fragments.Bind(BindingFragments.Identifier("Holder"), MethodCall("CallsItsParameter", one, Lambda())),
                "holder.CallsItsOwnHandlers()" => Fragments.Bind(BindingFragments.Identifier("holder"), MethodCall("CallsItsOwnHandlers")),
                "optional(one)" => Fragments.Bind(BindingFragments.Identifier("optional"), Invocation(one)),
                "maybe?.Handler(one)" => Fragments.Bind(BindingFragments.Identifier("maybe"), OptionalMethodCall("Handler", one)),
                "holder.Label(one)" => Fragments.Bind(BindingFragments.Identifier("holder"), MethodCall("Label", one)),
                "numbers.Count()" => Fragments.Bind(BindingFragments.Identifier("numbers"), MethodCall("Count")),
                "builder.Length(one)" => Fragments.Bind(BindingFragments.Identifier("builder"), MethodCall("Length", one)),
                "positive(one)" => Fragments.Bind(BindingFragments.Identifier("positive"), Invocation(one)),
                "clrHolder.Callback(one)" => Fragments.Bind(BindingFragments.Identifier("clrHolder"), MethodCall("Callback", one)),
                _ => throw new ArgumentOutOfRangeException(nameof(call), call, "Not a call this fixture spells."),
            };
        }

        public Expression BindBare(string kind, string name, IMistType type)
        {
            var bare = HandBuiltModel.Class("Bare");
            var scope = new BodyScope(bare.Scope);
            scope.DeclareLocal(new LocalVariable("one", Int32), HandBuiltModel.Node(), Fragments.Diagnostics);

            switch(kind)
            {
                case "field":
                    Field(bare, name, type);
                    break;
                case "property":
                    Property(bare, name, type);
                    break;
                default:
                    scope.DeclareLocal(new LocalVariable(name, type), HandBuiltModel.Node(), Fragments.Diagnostics);
                    break;
            }

            return PostfixBinder.BindPostfix(
                new PostfixNode { Target = BindingFragments.Identifier(name), Suffix = [Invocation(Reference("one"))] },
                new BindingContext(scope, null),
                Fragments.Builder);
        }

        public void DeclareClrShapes(bool importLinq)
        {
            var enumerable = new AbstractType("IEnumerable<>", "Standard", new Scope(null), null, null) { Assembly = MistAssembly.Ambient };
            var element = new TypeParameter("T", 0, enumerable.Scope, enumerable);
            enumerable.TypeParametersList.Add(element);

            var list = Ambient("List<>", "System.Collections.Generic");
            var item = new TypeParameter("T", 0, list.Scope, list);
            list.TypeParametersList.Add(item);
            list.BaseAbstractTypes.Add(Fragments.Built.Interner.GetOrCreateConstructedType(enumerable, [item]));
            Property(list, "Count", Int32, isClrProjected: true);

            var builder = Ambient("StringBuilder", "System.Text");
            Property(builder, "Length", Int32, isClrProjected: true);

            Extension("System.Linq", null, importLinq, block =>
            {
                var source = new TypeParameter("TSource", 0, block.Scope, null!);
                block.TypeParametersList.Add(source);
                block.ResolvedReceiverType = Fragments.Built.Interner.GetOrCreateConstructedType(enumerable, [source]);
                return new Method("Count", Int32, [], DeclarationFragments.Public with { IsClrProjected = true, ClrResultIsNonNull = true })
                {
                    Scope = new MethodScope(block.Scope) { IsStatic = true },
                };
            });

            var multicast = Ambient("MulticastDelegate", "System");
            var predicate = Delegate("Predicate<>", multicast, ["T"], parameters => parameters[0], _ => Fragments.Built.Type("Boolean"));
            var func = Delegate("Func<,>", multicast, ["T", "TResult"], parameters => parameters[0], parameters => parameters[1]);

            var clrHolder = HandBuiltModel.Class("ClrHolder");
            Property(clrHolder, "Callback", Fragments.Built.Interner.GetOrCreateConstructedType(func, [Int32, Int32]));

            _ = Fragments.Local("numbers", Fragments.Built.Interner.GetOrCreateConstructedType(list, [Int32]));
            _ = Fragments.Local("builder", builder);
            _ = Fragments.Local("positive", Fragments.Built.Interner.GetOrCreateConstructedType(predicate, [Int32]));
            _ = Fragments.Local("clrHolder", clrHolder);
        }

        private IMistType Optional(IMistType type)
        {
            return Fragments.Built.Interner.GetOrCreateUnionType([type, Fragments.Built.Type("Void")]);
        }

        private void Extension(string namespaceName, IMistType? receiver, bool imported, Func<ExtensionBlock, Method> declare)
        {
            var block = new ExtensionBlock("self", namespaceName, new ExtensionScope(null), null) { ResolvedReceiverType = receiver };
            block.MethodsList.Add(declare(block));

            var extensions = Fragments.Built.Model.GetOrAddNamespace(namespaceName).Extensions;
            if(!extensions.TryGetValue(block.ResolvedReceiverType!, out var blocks))
            {
                blocks = [];
                extensions[block.ResolvedReceiverType!] = blocks;
            }

            blocks.Add(block);

            if(imported)
            {
                Fragments.Types.DeclareImport(namespaceName);
            }
        }

        private static ClassType Ambient(string name, string namespaceName)
        {
            return new ClassType(name, namespaceName, new Scope(null), null, null) { Assembly = MistAssembly.Ambient };
        }

        private static ClassType Delegate(
            string name, ClassType multicast, string[] parameterNames,
            Func<IReadOnlyList<TypeParameter>, IMistType> input, Func<IReadOnlyList<TypeParameter>, IMistType> output)
        {
            var definition = Ambient(name, "System");
            definition.BaseType = multicast;
            var parameters = parameterNames.Select((parameterName, ordinal) => new TypeParameter(parameterName, ordinal, definition.Scope, definition)).ToList();
            definition.TypeParametersList.AddRange(parameters);
            definition.MethodsList.Add(new Method(
                "Invoke", output(parameters), [new Parameter("value", input(parameters))],
                DeclarationFragments.Public with { IsClrProjected = true, ClrResultIsNonNull = true })
            {
                DeclaringType = definition,
                Scope = definition.Scope,
            });
            return definition;
        }

        private static void Field(ConcreteType owner, string name, IMistType type, bool isStatic = false)
        {
            owner.FieldsList.Add(new Field(name, type, OwnershipKind.Unrestricted, DeclarationFragments.Public with { IsStatic = isStatic })
            {
                DeclaringType = owner,
            });
        }

        private static void Property(MemberType owner, string name, IMistType type, bool isClrProjected = false)
        {
            owner.PropertiesList.Add(new Property(name, type, DeclarationFragments.Public with { IsClrProjected = isClrProjected, ClrResultIsNonNull = isClrProjected })
            {
                DeclaringType = owner,
                Getter = new PropertyAccessor { IsAuto = true },
                Setter = new PropertyAccessor { IsAuto = true },
            });
        }

        private static ExpressionNode Reference(string local)
        {
            return BindingFragments.Expression(BindingFragments.Identifier(local));
        }

        private static ExpressionNode Lambda()
        {
            return new LambdaFormExpressionNode { Lambda = BindingFragments.Lambda(Reference("x")) };
        }

        private static MethodCallSuffixPostfixSuffixNode MethodCall(string name, params ExpressionNode[] arguments)
        {
            var suffix = BindingFragments.At<MethodCallSuffixPostfixSuffixNode>();
            suffix.Name = BindingFragments.Token(name);
            suffix.Arguments = BindingFragments.Arguments(arguments);
            return suffix;
        }

        private static OptionalMethodCallSuffixPostfixSuffixNode OptionalMethodCall(string name, params ExpressionNode[] arguments)
        {
            var suffix = BindingFragments.At<OptionalMethodCallSuffixPostfixSuffixNode>();
            suffix.Name = BindingFragments.Token(name);
            suffix.Arguments = BindingFragments.Arguments(arguments);
            return suffix;
        }

        private static InvocationSuffixPostfixSuffixNode Invocation(params ExpressionNode[] arguments)
        {
            var suffix = BindingFragments.At<InvocationSuffixPostfixSuffixNode>();
            suffix.Arguments = BindingFragments.Arguments(arguments);
            return suffix;
        }
    }
}