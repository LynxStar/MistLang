using System.Collections.Immutable;
using Mist.Generated;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Generators;
using Storm.Runtime;
using DiagnosticBag = Mist.Common.Diagnostics.DiagnosticBag;

namespace Mist.Tests.Helpers;

internal sealed class BindingFragments
{
    public HandBuiltModel Built { get; } = new();

    public DiagnosticBag Diagnostics { get; } = new();

    public BuilderContext Builder { get; }

    public BodyScope Scope { get; }

    public FileScope Types { get; }

    public BindingContext Binding { get; }

    public BindingFragments()
    {
        Builder = new BuilderContext(Built.Model, Diagnostics, new EmptyAmbient());
        Types = new FileScope(new ProgramScope(Built.Model), Built.Model, Diagnostics);
        Scope = new BodyScope(Types);
        Binding = new BindingContext(Scope, null);
        foreach(var name in new[] { "Int32", "Int64", "Int16", "UInt32", "Byte", "String", "Boolean", "Void", "Double", "Index", "Type" })
        {
            _ = Built.Model.RegisterType(Built.Type(name));
            Types.DeclareTopLevelType(name, Built.Type(name));
        }
    }

    public static T At<T>(int offset = 30) where T : SyntaxNode, new()
    {
        var location = HandBuiltModel.Node(offset);
        return new T { Span = location.Span, Origin = location.Origin };
    }

    public static Token Token(string text)
    {
        return MistTypeGenerator.MakeToken(text);
    }

    public static QualifiedIdentifierNode Name(string name)
    {
        return new() { IDENTIFIER = Token(name) };
    }

    public static TypeReferenceNode TypeName(string name)
    {
        return new()
        {
            First = new NamedSingleTypeRefSingleTypeRefNode { Name = Name(name) },
        };
    }

    public static IdentifierExpressionPrimaryNode Identifier(string name)
    {
        var node = At<IdentifierExpressionPrimaryNode>();
        node.Name = Token(name);
        return node;
    }

    public static ExpressionNode Expression(PrimaryNode primary, params PostfixSuffixNode[] suffixes)
    {
        var expression = MistAstGenerator.WrapPrimary(primary);
        ((PostfixExpressionUnaryNode)MistAstGenerator.ExtractUnary(expression)).Primary.Suffix = [.. suffixes];
        return expression;
    }

    public static ExpressionNode Expression(UnaryNode unary)
    {
        return new AssignmentFormExpressionNode
        {
            Assignment = new AssignmentNode
            {
                Target = new VoidCoalescingNode
                {
                    Left = new LogicalOrNode
                    {
                        Left = new LogicalAndNode
                        {
                            Left = new EqualityNode
                            {
                                Left = new RelationalComparisonRelationalNode
                                {
                                    Left = new AdditiveNode { Left = new MultiplicativeNode { Left = new NonRangeRangeNode { Operand = unary } } },
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    public static ArgumentListNode? Arguments(params ExpressionNode[] values)
    {
        if(values.Length == 0)
        {
            return null;
        }

        return new ArgumentListNode
        {
            Argument = new ValueArgumentArgumentNode { Expression = values[0] },
            Arguments = values.Skip(1).Select(value => (ArgumentNode)new ValueArgumentArgumentNode { Expression = value }).ToImmutableArray(),
        };
    }

    public static TypeArgumentListNode TypeArguments(params string[] names)
    {
        return new()
        {
            TypeArgument = new TypeReferenceTypeArgumentTypeArgumentNode { Type = TypeName(names[0]) },
            TypeArguments = names.Skip(1).Select(name => (TypeArgumentNode)new TypeReferenceTypeArgumentTypeArgumentNode { Type = TypeName(name) }).ToImmutableArray(),
        };
    }

    public Expression Local(string name, IMistType type, bool failed = false)
    {
        var local = new LocalVariable(name, type) { IsFailed = failed };
        Scope.DeclareLocal(local, Diagnostics);
        return new LocalReferenceExpression(HandBuiltModel.Node(30), local);
    }

    public void Register(IMistType type)
    {
        _ = Built.Model.RegisterType(type);
        Types.DeclareTopLevelType(type.Name, type);
    }

    public Method Function(string name, IMistType returns, params IMistType[] parameters)
    {
        var method = new Method(name, returns, parameters.Select((type, i) => new Parameter($"p{i}", type)).ToList(), new MemberModifiers())
        {
            Scope = Scope,
        };
        return method;
    }

    public static Method Member(MemberType owner, string name, IMistType returns, params IMistType[] parameters)
    {
        var method = new Method(name, returns, parameters.Select((type, i) => new Parameter($"p{i}", type)).ToList(), DeclarationFragments.Public)
        {
            DeclaringType = owner,
            Scope = owner.Scope,
        };
        owner.MethodsList.Add(method);
        return method;
    }

    public static TypeParameter Parameter(Method method, string name)
    {
        var parameter = new TypeParameter(name, method.TypeParametersList.Count, method.Scope!, null);
        method.TypeParametersList.Add(parameter);
        return parameter;
    }

    public static ExpressionLambdaLambdaExpressionNode Lambda(ExpressionNode result, string? statedType = null)
    {
        var node = At<ExpressionLambdaLambdaExpressionNode>();
        node.Params = statedType is null
            ? new SingleImplicitParamLambdaParameterListNode { Name = Token("x") }
            : new ParenthesizedLambdaParamsLambdaParameterListNode
            {
                Params = new LambdaParamListNode
                {
                    LambdaParam = new TypedLambdaParamLambdaParamNode { Name = Token("x"), Type = TypeName(statedType) },
                },
            };
        node.Body = result;
        return node;
    }

    public Expression Bind(PrimaryNode primary, params PostfixSuffixNode[] suffixes)
    {
        return PostfixBinder.BindPostfix(new PostfixNode { Target = primary, Suffix = [.. suffixes] }, Binding, Builder);
    }

    private sealed class EmptyAmbient : IAmbientTypeProvider
    {
        public void BuildAndRegister(ProgramModel.Model.ProgramModel programModel) { }
    }
}