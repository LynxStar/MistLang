using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Construction.Types;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Construction;

/// <summary>
/// A member may not share its enclosing type's name (MIST0344): the backend rejects that
/// declaration, and inside the type the bare name would resolve to the member. Each member builder
/// that names a member checks it, so each is driven here with the member it builds. An extension
/// block is not the type its members belong to, and the comparison is ordinal.
/// </summary>
[Trait("Tier", "2")]
public sealed class MemberNameConstructionTests
{
    private const int MemberOffset = 40;

    private readonly BindingFragments f = new();

    [Theory]
    [InlineData("field")]
    [InlineData("expression property")]
    [InlineData("block property")]
    [InlineData("method")]
    public void AMemberNamedAfterItsTypeIsRefusedOnceAtItAndStillRecorded(string kind)
    {
        var holder = BuildInClass(kind, "Holder");

        var diagnostic = Assert.Single(f.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST0344", diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(MemberOffset + 1, diagnostic.Position().Start.Column);
        Assert.Equal(new NameValue("Holder"), Assert.Single(diagnostic.Arguments));
        Assert.Equal("A member cannot have the same name as its enclosing type 'Holder'. Rename the member.", diagnostic.Message);
        Assert.Equal(["Holder"], MemberNames(holder));
    }

    [Theory]
    [InlineData("field")]
    [InlineData("expression property")]
    [InlineData("block property")]
    [InlineData("method")]
    public void AMemberNamedAfterItsTypeInAnotherCaseIsAdmitted(string kind)
    {
        var holder = BuildInClass(kind, "holder");

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal(["holder"], MemberNames(holder));
    }

    [Theory]
    [InlineData("expression property")]
    [InlineData("method")]
    public void AnExtensionMemberNamedAfterItsReceiverTypeIsAdmitted(string kind)
    {
        var block = ExceptionExtension();

        switch(kind)
        {
            case "method":
                BuildMethod(Method("Exception"), block);
                break;
            default:
                BuildProperty(ExpressionProperty("Exception"), block);
                break;
        }

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Equal(["Exception"], block.MethodsList.Select(method => method.Name).Concat(block.PropertiesList.Select(property => property.Name)));
    }

    private ClassType BuildInClass(string kind, string name)
    {
        switch(kind)
        {
            case "field":
                var body = BindingFragments.At<TypeBodyDeclarationNode>(0);
                body.Name = BindingFragments.Token("Holder");
                body.Keyword = new ClassKeywordTypeKeywordNode();
                body.Members = [new FieldMemberMemberDeclarationNode { Field = Field(name) }];
                var withField = new ClassType("Holder", "Probe", f.Types, null, new BodyTypeTypeDeclarationNode { TypeBody = body });
                new ConcreteTypeBuilder(f.Builder, f.Builder.Resolver).Build(withField);
                return withField;

            case "expression property":
                var withExpression = Class();
                BuildProperty(ExpressionProperty(name), withExpression);
                return withExpression;

            case "block property":
                var withBlock = Class();
                BuildProperty(BlockProperty(name), withBlock);
                return withBlock;

            case "method":
                var withMethod = Class();
                BuildMethod(Method(name), withMethod);
                return withMethod;

            default:
                throw new ArgumentException("Unknown member kind", nameof(kind));
        }
    }

    private static IEnumerable<string> MemberNames(ClassType holder)
    {
        return holder.FieldsList.Select(field => field.Name)
            .Concat(holder.PropertiesList.Select(property => property.Name))
            .Concat(holder.MethodsList.Select(method => method.Name));
    }

    private void BuildMethod(MethodDeclarationNode declaration, IMethodHost host)
    {
        var resolver = f.Builder.Resolver;
        var parameters = new ParameterBuilder(resolver, f.Builder.Attributes, f.Diagnostics);
        new MethodBuilder(f.Builder, resolver, new DelegateResolver(f.Builder, parameters, resolver), new ConstraintResolver(f.Builder, resolver))
            .Resolve(declaration, host);
    }

    private void BuildProperty(PropertyDeclarationNode declaration, IMethodHost host)
    {
        new PropertyBuilder(f.Builder, f.Builder.Resolver, new AccessorBuilder(f.Builder)).Resolve(declaration, host);
    }

    private ClassType Class()
    {
        return new ClassType("Holder", "Probe", f.Types, null, null);
    }

    private ExtensionBlock ExceptionExtension()
    {
        var assembly = new AssemblyScope(new ProgramScope(f.Built.Model), new MistAssembly("Owner", MistAssemblyKind.Self), f.Built.Model, new DiagnosticBag());
        var file = new FileScope(assembly, f.Built.Model, new DiagnosticBag());
        file.DeclareTopLevelType("Int32", f.Built.Type("Int32"));
        return new ExtensionBlock("self", "Probe", new ExtensionScope(file), null)
        {
            ResolvedReceiverType = new ClassType("Exception", "System", null, null, null),
        };
    }

    private static FieldDeclarationNode Field(string name)
    {
        var field = BindingFragments.At<FieldDeclarationNode>(MemberOffset);
        field.Type = BindingFragments.TypeName("Int32");
        field.Name = BindingFragments.Token(name);
        return field;
    }

    private static ExpressionBodiedPropertyPropertyDeclarationNode ExpressionProperty(string name)
    {
        var property = BindingFragments.At<ExpressionBodiedPropertyPropertyDeclarationNode>(MemberOffset);
        property.Modifiers = [new PublicModifierModifierNode()];
        property.Type = BindingFragments.TypeName("Int32");
        property.Name = BindingFragments.Token(name);
        property.Body = BindingFragments.Expression(BindingFragments.Identifier("value"));
        return property;
    }

    private static BlockPropertyPropertyDeclarationNode BlockProperty(string name)
    {
        var property = BindingFragments.At<BlockPropertyPropertyDeclarationNode>(MemberOffset);
        property.Modifiers = [new PublicModifierModifierNode()];
        property.Type = BindingFragments.TypeName("Int32");
        property.Name = BindingFragments.Token(name);
        property.Accessors = [BindingFragments.At<AutoGetAccessorAccessorDeclarationNode>(MemberOffset + 10)];
        return property;
    }

    private static MethodDeclarationNode Method(string name)
    {
        var method = BindingFragments.At<MethodDeclarationNode>(MemberOffset);
        method.Modifiers = [new PublicModifierModifierNode()];
        method.ReturnType = BindingFragments.TypeName("Int32");
        method.Name = BindingFragments.Token(name);
        method.Body = new CallableBlockBodyCallableBodyNode { Block = new BlockNode() };
        return method;
    }
}