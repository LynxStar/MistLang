using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

internal sealed class OptionalLambdaFragments
{
    public BindingFragments Binding { get; } = new();

    public FunctionType Signature { get; }

    public IMistType Target { get; }

    public TypeReferenceNode TargetSyntax { get; }

    public OptionalLambdaFragments(string spelling = "suffix", bool pure = false)
    {
        Signature = Binding.Built.Interner.GetOrCreateFunctionType([Binding.Built.Type("String")], Binding.Built.Type("String"), pure);
        var optional = Binding.Built.Interner.GetOrCreateUnionType([Signature, Binding.Built.Type("Void")]);
        RegisterAlias("Transform", Signature);
        RegisterAlias("MaybeTransform", optional);
        var alias = new TypeAlias("Optional<>", "Probe", Binding.Types, null, new TypeAliasDeclarationNode());
        var parameter = new TypeParameter("T", 0, alias.Scope, null);
        alias.TypeParametersList.Add(parameter);
        alias.AliasedType = new UnionType([parameter, Binding.Built.Type("Void")]);
        Binding.Register(alias);

        var function = new FuncSingleTypeRefSingleTypeRefNode
        {
            TypeArgs = BindingFragments.TypeArguments("String", "String"),
            Pure = pure ? BindingFragments.Token("pure") : null,
            Optional = spelling == "suffix" ? BindingFragments.Token("?") : null,
        };
        TargetSyntax = spelling switch
        {
            "union" => new TypeReferenceNode { First = function, SingleTypeRefs = [new VoidSingleTypeRefSingleTypeRefNode()] },
            "alias" => BindingFragments.TypeName("MaybeTransform"),
            "optional-alias" => new TypeReferenceNode
            {
                First = new NamedSingleTypeRefSingleTypeRefNode
                {
                    Name = BindingFragments.Name("Optional"),
                    TypeArgs = BindingFragments.TypeArguments("Transform"),
                },
            },
            _ => new TypeReferenceNode { First = function },
        };
        Target = Binding.Builder.Resolver.Resolve(TargetSyntax, Binding.Scope);
    }

    private void RegisterAlias(string name, IMistType type)
    {
        Binding.Register(new TypeAlias(name, "Probe", Binding.Types, null, new TypeAliasDeclarationNode()) { AliasedType = type });
    }

    public static LambdaFormExpressionNode Lambda(bool block, ExpressionNode? result = null, bool local = false)
    {
        result ??= BindingFragments.Expression(BindingFragments.Identifier("x"));
        var parameters = new SingleImplicitParamLambdaParameterListNode { Name = BindingFragments.Token("x") };
        var modifiers = local ? new LambdaModifierNode[] { new LocalLambdaModifierLambdaModifierNode() } : [];
        LambdaExpressionNode lambda = block
            ? new BlockLambdaLambdaExpressionNode
            {
                Params = parameters,
                Modifiers = [.. modifiers],
                Body = new BlockNode { Statements = [Return(result)] },
            }
            : new ExpressionLambdaLambdaExpressionNode { Params = parameters, Modifiers = [.. modifiers], Body = result };
        var location = HandBuiltModel.Node(30);
        lambda.Span = location.Span;
        lambda.Origin = location.Origin;
        return new LambdaFormExpressionNode { Lambda = lambda };
    }

    public static ReturnStatementStatementNode Return(ExpressionNode result)
    {
        return new ReturnStatementStatementNode { ReturnStatement = new ReturnStatementNode { Value = result } };
    }

    public Expression BindAt(string site, LambdaFormExpressionNode lambda)
    {
        var f = Binding;
        switch(site)
        {
            case "local":
                var statement = StatementBinder.BindStatement(new VariableDeclarationStatementStatementNode
                {
                    VariableDeclaration = new TypedVariableDeclarationVariableDeclarationStatementNode
                    {
                        Type = TargetSyntax,
                        Name = BindingFragments.Token("transform"),
                        Initializer = lambda,
                    },
                }, f.Binding, f.Builder);
                var declaration = (LocalDeclarationStatement)statement;
                return declaration.Initializer!;

            case "assignment":
                _ = f.Local("transform", Target);
                var assignment = (AssignmentFormExpressionNode)BindingFragments.Expression(BindingFragments.Identifier("transform"));
                assignment.Assignment.Group0 = new() { Operator = BindingFragments.Token("="), Value = lambda };
                return ((AssignmentExpression)ExpressionBinder.BindExpression(assignment, f.Binding, f.Builder)).Value;

            case "field":
                var owner = HandBuiltModel.Class("Holder");
                var field = new Field("transform", Target, OwnershipKind.Unrestricted, new MemberModifiers())
                {
                    DeclaringType = owner,
                    Initializer = new UnresolvedExpression(lambda),
                };
                field.Initializer = BodyBinder.BindInitializer(field.Initializer, field.Type, owner.Scope, f.Builder);
                return field.Initializer!;

            case "return":
                var body = new Block(new BlockNode { Statements = [Return(lambda)] });
                var method = new Method("MakeTransform", Target, [], new MemberModifiers()) { Scope = f.Scope, Body = body };
                BodyBinder.BindDelegate(method, f.Builder);
                return ((ReturnStatement)body.Statements[0]).Value!;

            case "argument":
                var api = HandBuiltModel.Class("Api");
                _ = BindingFragments.Member(api, "Apply", f.Built.Type("String"), Target);
                _ = f.Local("api", api);
                var call = f.Bind(BindingFragments.Identifier("api"), new MethodCallSuffixPostfixSuffixNode
                {
                    Name = BindingFragments.Token("Apply"),
                    Arguments = BindingFragments.Arguments(lambda),
                });
                return ((InvocationExpression)call).Arguments[0];

            default:
                throw new ArgumentOutOfRangeException(nameof(site));
        }
    }
}