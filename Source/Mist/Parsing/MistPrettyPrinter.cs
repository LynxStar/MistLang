using System.Collections.Immutable;
using System.Text;
using Mist.Generated;
using Storm.Runtime;

namespace Mist.Parsing;

/// <summary>
/// Formats a Mist AST back into syntactically valid Mist source text.
/// Operates on the AST only — no semantic model dependency.
/// Guarantees idempotence: print(parse(print(ast))) == print(ast). (R12.4)
/// </summary>
public sealed class MistPrettyPrinter
{
    private readonly StringBuilder _sb = new();
    private int _indentLevel;
    private const string IndentUnit = "    "; // 4 spaces per indent level
    private readonly string? _sourceText;

    private MistPrettyPrinter(string? sourceText = null)
    {
        _sourceText = sourceText;
    }

    /// <summary>
    /// Pretty-prints the given AST node to syntactically valid Mist source text.
    /// </summary>
    public static string Print(SyntaxNode ast)
    {
        var printer = new MistPrettyPrinter();
        printer.Visit(ast);
        return printer._sb.ToString();
    }

    /// <summary>
    /// Pretty-prints the given AST node using the original source text to reconstruct
    /// qualified names from span information. Use this overload for round-trip fidelity
    /// when working with ASTs produced by the parser.
    /// </summary>
    public static string Print(SyntaxNode ast, string sourceText)
    {
        var printer = new MistPrettyPrinter(sourceText);
        printer.Visit(ast);
        return printer._sb.ToString();
    }

    private void Visit(SyntaxNode node)
    {
        switch (node)
        {
            case CompilationUnitNode cu:
                EmitCompilationUnit(cu);
                break;
            default:
                // For any other node type, attempt to emit it directly.
                break;
        }
    }

    // ─── Compilation Unit ────────────────────────────────────────────────

    private void EmitCompilationUnit(CompilationUnitNode cu)
    {
        foreach (var usingDecl in cu.Usings)
        {
            EmitUsingDeclaration(usingDecl);
        }

        if (cu.Usings.Length > 0 && cu.Items.Length > 0)
        {
            AppendLine();
        }

        foreach (var item in cu.Items)
        {
            switch (item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                    EmitNamespaceDeclaration(nsItem.NamespaceDeclaration);
                    break;
                case TypeItemTopLevelItemNode typeItem:
                    EmitTypeDeclaration(typeItem.TypeDeclaration);
                    break;
                case WithItemTopLevelItemNode withItem:
                    EmitWithDeclaration(withItem.WithDeclaration);
                    break;
            }
        }
    }

    // ─── Using Declarations ──────────────────────────────────────────────

    private void EmitUsingDeclaration(UsingDeclarationNode usingDecl)
    {
        var name = ExtractQualifiedName(usingDecl.Name);
        AppendLine($"using {name};");
    }

    // ─── Namespace Declarations ──────────────────────────────────────────

    private void EmitNamespaceDeclaration(NamespaceDeclarationNode ns)
    {
        switch (ns)
        {
            case FileScopedNamespaceNamespaceDeclarationNode fileScopedNs:
            {
                var name = ExtractQualifiedName(fileScopedNs.Name);
                AppendLine($"namespace {name};");
                AppendLine();

                foreach (var typeDecl in fileScopedNs.Body)
                {
                    EmitTypeDeclaration(typeDecl);
                }
                break;
            }
            case BlockNamespaceNamespaceDeclarationNode blockNs:
            {
                var name = ExtractQualifiedName(blockNs.Name);
                AppendLine($"namespace {name} {{");
                _indentLevel++;

                foreach (var typeDecl in blockNs.Body)
                {
                    EmitTypeDeclaration(typeDecl);
                }

                _indentLevel--;
                AppendLine("}");
                break;
            }
        }
    }

    // ─── Type Declarations ───────────────────────────────────────────────

    private void EmitTypeDeclaration(TypeDeclarationNode typeDecl)
    {
        switch (typeDecl)
        {
            case ClassTypeTypeDeclarationNode classType:
                EmitClassDeclaration(classType.ClassDeclaration);
                break;
            case StructTypeTypeDeclarationNode structType:
                EmitStructDeclaration(structType.StructDeclaration);
                break;
            case InterfaceTypeTypeDeclarationNode interfaceType:
                EmitInterfaceDeclaration(interfaceType.InterfaceDeclaration);
                break;
            case TraitTypeTypeDeclarationNode traitType:
                EmitTraitDeclaration(traitType.TraitDeclaration);
                break;
        }
    }

    private void EmitClassDeclaration(ClassDeclarationNode classDecl)
    {
        var modifiers = EmitModifiers(classDecl.Modifiers);
        var name = classDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(classDecl.TypeParams);
        var baseClause = "";
        if (classDecl.BaseList is not null)
        {
            baseClause = $" : {EmitBaseList(classDecl.BaseList)}";
        }
        AppendLine($"{modifiers}class {name}{typeParams}{baseClause} {{");
        _indentLevel++;

        EmitMembers(classDecl.Members);

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitStructDeclaration(StructDeclarationNode structDecl)
    {
        var modifiers = EmitModifiers(structDecl.Modifiers);
        var name = structDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(structDecl.TypeParams);
        var interfaceClause = "";
        if (structDecl.Interfaces is not null)
        {
            interfaceClause = $" : {EmitBaseList(structDecl.Interfaces)}";
        }
        AppendLine($"{modifiers}struct {name}{typeParams}{interfaceClause} {{");
        _indentLevel++;

        EmitMembers(structDecl.Members);

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitInterfaceDeclaration(InterfaceDeclarationNode interfaceDecl)
    {
        var name = interfaceDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(interfaceDecl.TypeParams);
        var interfaceClause = "";
        if (interfaceDecl.Interfaces is not null)
        {
            interfaceClause = $" : {EmitBaseList(interfaceDecl.Interfaces)}";
        }
        AppendLine($"interface {name}{typeParams}{interfaceClause} {{");
        _indentLevel++;

        for (var i = 0; i < interfaceDecl.Members.Length; i++)
        {
            EmitInterfaceMemberDeclaration(interfaceDecl.Members[i]);
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitInterfaceMethodDeclaration(InterfaceMethodDeclarationNode methodDecl)
    {
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(methodDecl.TypeParams);
        var parameters = EmitParameterList(methodDecl.Parameters);
        AppendLine($"{returnType} {name}{typeParams}({parameters});");
    }

    private void EmitMembers(ImmutableArray<MemberDeclarationNode> members)
    {
        for (var i = 0; i < members.Length; i++)
        {
            if (i > 0)
            {
                AppendLine();
            }
            EmitMemberDeclaration(members[i]);
        }
    }

    // ─── Member Declarations ─────────────────────────────────────────────

    private void EmitMemberDeclaration(MemberDeclarationNode memberDecl)
    {
        switch (memberDecl)
        {
            case FieldMemberMemberDeclarationNode fieldMember:
                EmitFieldDeclaration(fieldMember.Field);
                break;
            case ConstructorMemberMemberDeclarationNode ctorMember:
                EmitConstructorDeclaration(ctorMember.Constructor);
                break;
            case MethodMemberMemberDeclarationNode methodMember:
                EmitMethodDeclaration(methodMember.Method);
                break;
            case PropertyMemberMemberDeclarationNode propertyMember:
                EmitPropertyDeclaration(propertyMember.Property);
                break;
            case IndexerMemberMemberDeclarationNode indexerMember:
                EmitIndexerDeclaration(indexerMember.Indexer);
                break;
            case OperatorMemberMemberDeclarationNode operatorMember:
                EmitOperatorDeclaration(operatorMember.Operator);
                break;
            case CastOperatorMemberMemberDeclarationNode castOpMember:
                EmitCastOperatorDeclaration(castOpMember.CastOperator);
                break;
        }
    }

    private void EmitFieldDeclaration(FieldDeclarationNode fieldDecl)
    {
        var modifiers = EmitModifiers(fieldDecl.Modifiers);
        var type = EmitTypeReference(fieldDecl.Type);
        var name = fieldDecl.Name.Lexeme;

        if (fieldDecl.Initializer is not null)
        {
            AppendLine($"{modifiers}{type} {name} = {EmitExpression(fieldDecl.Initializer)};");
        }
        else
        {
            AppendLine($"{modifiers}{type} {name};");
        }
    }

    private void EmitConstructorDeclaration(ConstructorDeclarationNode ctorDecl)
    {
        var modifiers = EmitModifiers(ctorDecl.Modifiers);
        var name = ctorDecl.Name.Lexeme;
        var parameters = EmitParameterList(ctorDecl.Parameters);

        var chain = "";
        if (ctorDecl.Chain is ThisChainConstructorChainNode thisChain)
        {
            chain = " : this(" + EmitArgumentList(thisChain.Arguments) + ")";
        }
        else if (ctorDecl.Chain is BaseChainConstructorChainNode baseChain)
        {
            chain = " : base(" + EmitArgumentList(baseChain.Arguments) + ")";
        }

        AppendLine($"{modifiers}{name}({parameters}){chain} {{");
        _indentLevel++;

        if (ctorDecl.Body.GetBlock() is BlockNode ctorBlock)
        {
            foreach (var stmt in ctorBlock.Statements)
            {
                EmitStatement(stmt);
            }
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitMethodDeclaration(MethodDeclarationNode methodDecl)
    {
        var modifiers = EmitModifiers(methodDecl.Modifiers);
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(methodDecl.TypeParams);
        var parameters = EmitParameterList(methodDecl.Parameters);

        AppendLine($"{modifiers}{returnType} {name}{typeParams}({parameters}) {{");
        _indentLevel++;

        if (methodDecl.Body.GetBlock() is BlockNode methodBlock)
        {
            foreach (var stmt in methodBlock.Statements)
            {
                EmitStatement(stmt);
            }
        }

        _indentLevel--;
        AppendLine("}");
    }

    // ─── Property Declarations ──────────────────────────────────────────

    private void EmitPropertyDeclaration(PropertyDeclarationNode propertyDecl)
    {
        switch (propertyDecl)
        {
            case BlockPropertyPropertyDeclarationNode blockProp:
                EmitBlockPropertyDeclaration(blockProp);
                break;
            case ExpressionBodiedPropertyPropertyDeclarationNode exprProp:
                EmitExpressionBodiedPropertyDeclaration(exprProp);
                break;
        }
    }

    private void EmitBlockPropertyDeclaration(BlockPropertyPropertyDeclarationNode blockProp)
    {
        var modifiers = EmitModifiers(blockProp.Modifiers);
        var type = EmitTypeReference(blockProp.Type);
        var name = blockProp.Name.Lexeme;

        // Build accessor list.
        var accessorParts = new List<string>();
        foreach (var accessor in blockProp.Accessors)
        {
            accessorParts.Add(EmitAccessorDeclaration(accessor));
        }

        var accessorBody = string.Join(" ", accessorParts);

        // Emit initializer if present.
        if (blockProp.Initializer is not null)
        {
            var init = EmitExpression(blockProp.Initializer);
            AppendLine($"{modifiers}{type} {name} {{ {accessorBody} }} = {init};");
        }
        else
        {
            AppendLine($"{modifiers}{type} {name} {{ {accessorBody} }}");
        }
    }

    private void EmitExpressionBodiedPropertyDeclaration(ExpressionBodiedPropertyPropertyDeclarationNode exprProp)
    {
        var modifiers = EmitModifiers(exprProp.Modifiers);
        var type = EmitTypeReference(exprProp.Type);
        var name = exprProp.Name.Lexeme;
        var body = EmitExpression(exprProp.Body);

        AppendLine($"{modifiers}{type} {name} => {body};");
    }

    private string EmitAccessorDeclaration(AccessorDeclarationNode accessor)
    {
        switch (accessor)
        {
            // ── Get accessors ──
            case AutoGetAccessorAccessorDeclarationNode autoGet:
            {
                var mod = EmitAccessorModifier(autoGet.AccessorModifier);
                return $"{mod}get;";
            }
            case BlockGetAccessorAccessorDeclarationNode blockGet:
            {
                var mod = EmitAccessorModifier(blockGet.AccessorModifier);
                var body = EmitBlockBody(blockGet.Body);
                return $"{mod}get {body}";
            }
            case ExpressionGetAccessorAccessorDeclarationNode exprGet:
            {
                var mod = EmitAccessorModifier(exprGet.AccessorModifier);
                var expr = EmitExpression(exprGet.Expression);
                return $"{mod}get => {expr};";
            }

            // ── Set accessors ──
            case AutoSetAccessorAccessorDeclarationNode autoSet:
            {
                var mod = EmitAccessorModifier(autoSet.AccessorModifier);
                return $"{mod}set;";
            }
            case BlockSetAccessorAccessorDeclarationNode blockSet:
            {
                var mod = EmitAccessorModifier(blockSet.AccessorModifier);
                var body = EmitBlockBody(blockSet.Body);
                return $"{mod}set {body}";
            }
            case ExpressionSetAccessorAccessorDeclarationNode exprSet:
            {
                var mod = EmitAccessorModifier(exprSet.AccessorModifier);
                var expr = EmitExpression(exprSet.Expression);
                return $"{mod}set => {expr};";
            }

            // ── Init accessors ──
            case AutoInitAccessorAccessorDeclarationNode autoInit:
            {
                var mod = EmitAccessorModifier(autoInit.AccessorModifier);
                return $"{mod}init;";
            }
            case BlockInitAccessorAccessorDeclarationNode blockInit:
            {
                var mod = EmitAccessorModifier(blockInit.AccessorModifier);
                var body = EmitBlockBody(blockInit.Body);
                return $"{mod}init {body}";
            }
            case ExpressionInitAccessorAccessorDeclarationNode exprInit:
            {
                var mod = EmitAccessorModifier(exprInit.AccessorModifier);
                var expr = EmitExpression(exprInit.Expression);
                return $"{mod}init => {expr};";
            }

            default:
                return "";
        }
    }

    private static string EmitAccessorModifier(ModifierNode? modifier)
    {
        if (modifier is null) return "";

        return modifier switch
        {
            PublicModifierModifierNode => "public ",
            PrivateModifierModifierNode => "private ",
            StaticModifierModifierNode => "static ",
            _ => "",
        };
    }

    /// <summary>
    /// Emits a block body as an inline string for use in accessor declarations.
    /// Produces "{ stmt1; stmt2; }" format.
    /// </summary>
    private string EmitBlockBody(BlockNode block)
    {
        if (block.Statements.IsEmpty)
        {
            return "{ }";
        }

        var parts = new List<string>();
        foreach (var stmt in block.Statements)
        {
            parts.Add(EmitStatementInline(stmt));
        }

        return $"{{ {string.Join(" ", parts)} }}";
    }

    /// <summary>
    /// Emits a statement as an inline string (no indentation/newline).
    /// Used for accessor block bodies that are emitted on a single line.
    /// </summary>
    private string EmitStatementInline(StatementNode stmt)
    {
        switch (stmt)
        {
            case ReturnStatementStatementNode returnStmt:
            {
                if (returnStmt.ReturnStatement.Value is not null)
                {
                    return $"return {EmitExpression(returnStmt.ReturnStatement.Value)};";
                }
                return "return;";
            }
            case ExpressionStatementStatementNode exprStmt:
                return $"{EmitExpression(exprStmt.ExpressionStatement.Expression)};";
            case VariableDeclarationStatementStatementNode varDeclStmt:
            {
                switch (varDeclStmt.VariableDeclaration)
                {
                    case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                    {
                        var type = EmitTypeReference(typed.Type);
                        var name = typed.Name.Lexeme;
                        var init = EmitExpression(typed.Initializer);
                        return $"{type} {name} = {init};";
                    }
                    case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                    {
                        var name = inferred.Name.Lexeme;
                        var init = EmitExpression(inferred.Initializer);
                        return $"var {name} = {init};";
                    }
                    default:
                        return "";
                }
            }
            case IfStatementStatementNode ifStmt:
            {
                var condition = EmitExpression(ifStmt.IfStatement.Condition);
                return $"if ({condition}) {{ ... }}";
            }
            case WhileStatementStatementNode whileStmt:
            {
                var condition = EmitExpression(whileStmt.WhileStatement.Condition);
                return $"while ({condition}) {{ ... }}";
            }
            case BreakStatementStatementNode:
                return "break;";
            default:
                return "";
        }
    }

    // ─── Indexer Declarations ────────────────────────────────────────────

    private void EmitIndexerDeclaration(IndexerDeclarationNode indexerDecl)
    {
        if (indexerDecl is not BlockIndexerIndexerDeclarationNode blockIndexer)
            return;

        var modifiers = EmitModifiers(blockIndexer.Modifiers);
        var type = EmitTypeReference(blockIndexer.Type);
        var parameters = EmitParameterList(blockIndexer.Parameters);

        // Build accessor list.
        var accessorParts = new List<string>();
        foreach (var accessor in blockIndexer.Accessors)
        {
            accessorParts.Add(EmitAccessorDeclaration(accessor));
        }

        var accessorBody = string.Join(" ", accessorParts);
        AppendLine($"{modifiers}{type} this[{parameters}] {{ {accessorBody} }}");
    }

    // ─── Operator Declarations ───────────────────────────────────────────

    private void EmitOperatorDeclaration(OperatorDeclarationNode opDecl)
    {
        var modifiers = EmitModifiers(opDecl.Modifiers);
        var returnType = EmitTypeReference(opDecl.ReturnType);
        var symbol = opDecl.Symbol switch
        {
            PlusOpOperatorSymbolNode => "+",
            MinusOpOperatorSymbolNode => "-",
            StarOpOperatorSymbolNode => "*",
            SlashOpOperatorSymbolNode => "/",
            ModuloOpOperatorSymbolNode => "%",
            EqualEqualOpOperatorSymbolNode => "==",
            NotEqualOpOperatorSymbolNode => "!=",
            LessThanOpOperatorSymbolNode => "<",
            GreaterThanOpOperatorSymbolNode => ">",
            LessThanOrEqualOpOperatorSymbolNode => "<=",
            GreaterThanOrEqualOpOperatorSymbolNode => ">=",
            LogicalAndOpOperatorSymbolNode => "&&",
            LogicalOrOpOperatorSymbolNode => "||",
            ExclamationOpOperatorSymbolNode => "!",
            _ => "?",
        };
        var parameters = EmitParameterList(opDecl.Parameters);

        if (opDecl.Body is OperatorBlockBodyOperatorBodyNode blockBody)
        {
            AppendLine($"{modifiers}{returnType} operator {symbol}({parameters}) {{");
            _indentLevel++;

            foreach (var stmt in blockBody.Block.Statements)
            {
                EmitStatement(stmt);
            }

            _indentLevel--;
            AppendLine("}");
        }
        else
        {
            AppendLine($"{modifiers}{returnType} operator {symbol}({parameters});");
        }
    }

    private void EmitCastOperatorDeclaration(CastOperatorDeclarationNode castOpDecl)
    {
        var modifiers = EmitModifiers(castOpDecl.Modifiers);
        var castKind = castOpDecl.CastKind is ImplicitCastCastOperatorKindNode
            ? "implicit" : "explicit";
        var targetType = EmitTypeReference(castOpDecl.TargetType);
        var parameters = EmitParameterList(castOpDecl.Parameters);

        if (castOpDecl.Body is OperatorBlockBodyOperatorBodyNode blockBody)
        {
            AppendLine($"{modifiers}{castKind} operator {targetType}({parameters}) {{");
            _indentLevel++;

            foreach (var stmt in blockBody.Block.Statements)
            {
                EmitStatement(stmt);
            }

            _indentLevel--;
            AppendLine("}");
        }
        else
        {
            AppendLine($"{modifiers}{castKind} operator {targetType}({parameters});");
        }
    }

    // ─── Interface Property Declarations ─────────────────────────────────

    private void EmitInterfaceMemberDeclaration(InterfaceMemberDeclarationNode memberDecl)
    {
        switch (memberDecl)
        {
            case InterfaceMethodMemberInterfaceMemberDeclarationNode methodMember:
                EmitInterfaceMethodDeclaration(methodMember.Method);
                break;
            case InterfacePropertyMemberInterfaceMemberDeclarationNode propertyMember:
                EmitInterfacePropertyDeclaration(propertyMember.Property);
                break;
        }
    }

    private void EmitInterfacePropertyDeclaration(InterfacePropertyDeclarationNode propertyDecl)
    {
        var type = EmitTypeReference(propertyDecl.Type);
        var name = propertyDecl.Name.Lexeme;

        var accessors = new List<string>();
        foreach (var accessor in propertyDecl.Accessors)
        {
            switch (accessor)
            {
                case InterfaceGetAccessorInterfaceAccessorDeclarationNode:
                    accessors.Add("get;");
                    break;
                case InterfaceSetAccessorInterfaceAccessorDeclarationNode:
                    accessors.Add("set;");
                    break;
            }
        }

        AppendLine($"{type} {name} {{ {string.Join(" ", accessors)} }}");
    }

    // ─── Trait Declarations ─────────────────────────────────────────────

    private void EmitTraitDeclaration(TraitDeclarationNode traitDecl)
    {
        var name = traitDecl.Name.Lexeme;
        AppendLine($"trait {name} {{");
        _indentLevel++;

        for (var i = 0; i < traitDecl.Members.Length; i++)
        {
            EmitTraitMemberDeclaration(traitDecl.Members[i]);
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitTraitMemberDeclaration(TraitMemberDeclarationNode memberDecl)
    {
        switch (memberDecl)
        {
            case TraitFieldMemberTraitMemberDeclarationNode fieldMember:
                EmitTraitFieldDeclaration(fieldMember.Field);
                break;
            case TraitMethodMemberTraitMemberDeclarationNode methodMember:
                EmitTraitMethodDeclaration(methodMember.Method);
                break;
            case TraitPropertyMemberTraitMemberDeclarationNode propertyMember:
                EmitTraitPropertyDeclaration(propertyMember.Property);
                break;
        }
    }

    private void EmitTraitFieldDeclaration(TraitFieldDeclarationNode fieldDecl)
    {
        var type = EmitTypeReference(fieldDecl.Type);
        var name = fieldDecl.Name.Lexeme;
        AppendLine($"{type} {name};");
    }

    private void EmitTraitMethodDeclaration(TraitMethodDeclarationNode methodDecl)
    {
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var parameters = EmitParameterList(methodDecl.Parameters);
        AppendLine($"{returnType} {name}({parameters});");
    }

    private void EmitTraitPropertyDeclaration(TraitPropertyDeclarationNode propertyDecl)
    {
        var type = EmitTypeReference(propertyDecl.Type);
        var name = propertyDecl.Name.Lexeme;

        var accessors = new List<string>();
        foreach (var accessor in propertyDecl.Accessors)
        {
            switch (accessor)
            {
                case InterfaceGetAccessorInterfaceAccessorDeclarationNode:
                    accessors.Add("get;");
                    break;
                case InterfaceSetAccessorInterfaceAccessorDeclarationNode:
                    accessors.Add("set;");
                    break;
            }
        }

        AppendLine($"{type} {name} {{ {string.Join(" ", accessors)} }}");
    }

    // ─── With Declarations ──────────────────────────────────────────────

    private void EmitWithDeclaration(WithDeclarationNode withDecl)
    {
        var typeName = withDecl.TypeName.Lexeme;
        var traitName = withDecl.TraitName.Lexeme;
        AppendLine($"with {typeName} : {traitName} {{");
        _indentLevel++;

        foreach (var mapping in withDecl.Mappings)
        {
            EmitMemberMapping(mapping);
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitMemberMapping(MemberMappingNode mapping)
    {
        var sourceName = mapping.SourceName.Lexeme;
        var targetName = mapping.TargetName.Lexeme;
        AppendLine($"{sourceName} as {targetName};");
    }

    // ─── Parameters and Arguments ────────────────────────────────────────

    private string EmitParameterList(ParameterListNode? paramList)
    {
        if (paramList is null) return "";

        var parameters = new List<string>();
        parameters.Add(EmitParameter(paramList.Parameter));

        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode param && !ReferenceEquals(param, paramList.Parameter))
            {
                parameters.Add(EmitParameter(param));
            }
        }

        return string.Join(", ", parameters);
    }

    private string EmitParameter(ParameterNode param)
    {
        var type = EmitTypeReference(param.Type);
        var name = param.Name.Lexeme;
        return $"{type} {name}";
    }

    private string EmitArgumentList(ArgumentListNode? argList)
    {
        if (argList is null) return "";

        var arguments = new List<string>();
        arguments.Add(EmitExpression(argList.Expression));

        foreach (var child in argList.Children())
        {
            if (child is ExpressionNode expr && !ReferenceEquals(expr, argList.Expression))
            {
                arguments.Add(EmitExpression(expr));
            }
        }

        return string.Join(", ", arguments);
    }

    // ─── Type References ─────────────────────────────────────────────────

    private string EmitTypeReference(TypeReferenceNode typeRef)
    {
        switch (typeRef)
        {
            case PrimitiveTypeRefTypeReferenceNode primitiveRef:
            {
                var keyword = EmitPrimitiveType(primitiveRef.PrimitiveType);
                if (primitiveRef.ArrayRank.HasValue)
                {
                    return $"{keyword}[]";
                }
                return keyword;
            }
            case VoidTypeRefTypeReferenceNode:
                return "void";
            case NamedTypeRefTypeReferenceNode namedRef:
            {
                var name = ExtractQualifiedName(namedRef.Name);
                var typeArgs = EmitTypeArgumentList(namedRef.TypeArgs);
                if (namedRef.ArrayRank.HasValue)
                {
                    return $"{name}{typeArgs}[]";
                }
                return $"{name}{typeArgs}";
            }
            default:
                return "object";
        }
    }

    private static string EmitPrimitiveType(PrimitiveTypeNode primitiveType)
    {
        return primitiveType switch
        {
            IntTypePrimitiveTypeNode => "int",
            UintTypePrimitiveTypeNode => "uint",
            LongTypePrimitiveTypeNode => "long",
            UlongTypePrimitiveTypeNode => "ulong",
            ShortTypePrimitiveTypeNode => "short",
            UshortTypePrimitiveTypeNode => "ushort",
            ByteTypePrimitiveTypeNode => "byte",
            SbyteTypePrimitiveTypeNode => "sbyte",
            FloatTypePrimitiveTypeNode => "float",
            DoubleTypePrimitiveTypeNode => "double",
            DecimalTypePrimitiveTypeNode => "decimal",
            BoolTypePrimitiveTypeNode => "bool",
            CharTypePrimitiveTypeNode => "char",
            StringTypePrimitiveTypeNode => "string",
            _ => "object",
        };
    }

    // ─── Statements ──────────────────────────────────────────────────────

    private void EmitStatement(StatementNode stmt)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDeclStmt:
                EmitVariableDeclaration(varDeclStmt.VariableDeclaration);
                break;
            case ReturnStatementStatementNode returnStmt:
                EmitReturnStatement(returnStmt.ReturnStatement);
                break;
            case IfStatementStatementNode ifStmt:
                EmitIfStatement(ifStmt.IfStatement);
                break;
            case WhileStatementStatementNode whileStmt:
                EmitWhileStatement(whileStmt.WhileStatement);
                break;
            case ForStatementStatementNode forStmt:
                EmitForStatement(forStmt.ForStatement);
                break;
            case ForeachStatementStatementNode foreachStmt:
                EmitForeachStatement(foreachStmt.ForeachStatement);
                break;
            case BreakStatementStatementNode:
                AppendLine("break;");
                break;
            case ExpressionStatementStatementNode exprStmt:
                AppendLine($"{EmitExpression(exprStmt.ExpressionStatement.Expression)};");
                break;
        }
    }

    private void EmitVariableDeclaration(VariableDeclarationStatementNode varDecl)
    {
        switch (varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
            {
                var type = EmitTypeReference(typed.Type);
                var name = typed.Name.Lexeme;
                var init = EmitExpression(typed.Initializer);
                AppendLine($"{type} {name} = {init};");
                break;
            }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
            {
                var name = inferred.Name.Lexeme;
                var init = EmitExpression(inferred.Initializer);
                AppendLine($"var {name} = {init};");
                break;
            }
        }
    }

    private void EmitReturnStatement(ReturnStatementNode returnStmt)
    {
        if (returnStmt.Value is not null)
        {
            AppendLine($"return {EmitExpression(returnStmt.Value)};");
        }
        else
        {
            AppendLine("return;");
        }
    }

    private void EmitIfStatement(IfStatementNode ifStmt)
    {
        var condition = EmitExpression(ifStmt.Condition);
        AppendLine($"if ({condition}) {{");
        _indentLevel++;

        foreach (var stmt in ifStmt.ThenBranch.Statements)
        {
            EmitStatement(stmt);
        }

        _indentLevel--;

        if (ifStmt.ElseBranch is not null)
        {
            EmitElseClause(ifStmt.ElseBranch);
        }
        else
        {
            AppendLine("}");
        }
    }

    private void EmitElseClause(ElseClauseNode elseClause)
    {
        switch (elseClause)
        {
            case ElseIfElseClauseNode elseIf:
            {
                var condition = EmitExpression(elseIf.ElseIf.Condition);
                AppendLine($"}} else if ({condition}) {{");
                _indentLevel++;

                foreach (var stmt in elseIf.ElseIf.ThenBranch.Statements)
                {
                    EmitStatement(stmt);
                }

                _indentLevel--;

                if (elseIf.ElseIf.ElseBranch is not null)
                {
                    EmitElseClause(elseIf.ElseIf.ElseBranch);
                }
                else
                {
                    AppendLine("}");
                }
                break;
            }
            case ElseBlockElseClauseNode elseBlock:
            {
                AppendLine("} else {");
                _indentLevel++;

                foreach (var stmt in elseBlock.ElseBlock.Statements)
                {
                    EmitStatement(stmt);
                }

                _indentLevel--;
                AppendLine("}");
                break;
            }
        }
    }

    private void EmitWhileStatement(WhileStatementNode whileStmt)
    {
        var condition = EmitExpression(whileStmt.Condition);
        AppendLine($"while ({condition}) {{");
        _indentLevel++;

        foreach (var stmt in whileStmt.Body.Statements)
        {
            EmitStatement(stmt);
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitForStatement(ForStatementNode forStmt)
    {
        var init = EmitForInitializer(forStmt.Initializer);
        var condition = forStmt.Condition is not null ? EmitExpression(forStmt.Condition) : "";
        var iterator = forStmt.Iterator is not null ? EmitExpression(forStmt.Iterator) : "";

        AppendLine($"for ({init}; {condition}; {iterator}) {{");
        _indentLevel++;

        foreach (var stmt in forStmt.Body.Statements)
        {
            EmitStatement(stmt);
        }

        _indentLevel--;
        AppendLine("}");
    }

    private string EmitForInitializer(ForInitializerNode? initializer)
    {
        if (initializer is null) return "";

        switch (initializer)
        {
            case ForInitializerVariableDeclarationForInitializerNode varDecl:
            {
                var type = EmitTypeReference(varDecl.VariableDeclaration);
                var name = varDecl.Name.Lexeme;
                var init = EmitExpression(varDecl.Initializer);
                return $"{type} {name} = {init}";
            }
            case ForInitializerExpressionForInitializerNode exprInit:
                return EmitExpression(exprInit.Expression);
            default:
                return "";
        }
    }

    private void EmitForeachStatement(ForeachStatementNode foreachStmt)
    {
        var type = EmitTypeReference(foreachStmt.Type);
        var name = foreachStmt.Name.Lexeme;
        var collection = EmitExpression(foreachStmt.Collection);

        AppendLine($"foreach ({type} {name} in {collection}) {{");
        _indentLevel++;

        foreach (var stmt in foreachStmt.Body.Statements)
        {
            EmitStatement(stmt);
        }

        _indentLevel--;
        AppendLine("}");
    }

    // ─── Expression Emission ─────────────────────────────────────────────

    private string EmitExpression(ExpressionNode expr)
    {
        return EmitAssignment(expr.Assignment);
    }

    private string EmitAssignment(AssignmentNode assignment)
    {
        var target = EmitLogicalOr(assignment.Target);

        if (assignment.Value is not null && !ReferenceEquals(assignment.Value.Target, assignment.Target))
        {
            var value = EmitAssignment(assignment.Value);
            return $"{target} = {value}";
        }

        return target;
    }

    private string EmitLogicalOr(LogicalOrNode logicalOr)
    {
        var left = EmitLogicalAnd(logicalOr.Left);

        if (logicalOr.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            var right = EmitLogicalAnd(logicalOr.Right);
            return $"{left} || {right}";
        }

        return left;
    }

    private string EmitLogicalAnd(LogicalAndNode logicalAnd)
    {
        var left = EmitEquality(logicalAnd.Left);

        if (logicalAnd.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            var right = EmitEquality(logicalAnd.Right);
            return $"{left} && {right}";
        }

        return left;
    }

    private string EmitEquality(EqualityNode equality)
    {
        var left = EmitRelational(equality.Left);

        if (equality.Right is not null && !ReferenceEquals(equality.Right, equality.Left))
        {
            var op = equality.Operator.Lexeme;
            var right = EmitRelational(equality.Right);
            return $"{left} {op} {right}";
        }

        return left;
    }

    private string EmitRelational(RelationalNode relational)
    {
        switch (relational)
        {
            case RelationalComparisonRelationalNode cmp:
            {
                var left = EmitAdditive(cmp.Left);

                if (cmp.Right is not null && !ReferenceEquals(cmp.Right, cmp.Left))
                {
                    var op = cmp.Operator.Lexeme;
                    var right = EmitAdditive(cmp.Right);
                    return $"{left} {op} {right}";
                }

                return left;
            }

            case IsExpressionRelationalNode isExpr:
            {
                var scrutinee = EmitAdditive(isExpr.Left);
                var pattern = EmitPattern(isExpr.Pattern);
                return $"{scrutinee} is {pattern}";
            }

            default:
                return "";
        }
    }

    // ─── Pattern Emission ────────────────────────────────────────────────

    private string EmitPattern(PatternNode pattern)
    {
        return EmitOrPattern(pattern.OrPattern);
    }

    private string EmitOrPattern(OrPatternNode orPattern)
    {
        var left = EmitAndPattern(orPattern.Left);

        if (orPattern.Right is null || ReferenceEquals(orPattern.Right, orPattern.Left))
            return left;

        var right = EmitAndPattern(orPattern.Right);
        return $"{left} or {right}";
    }

    private string EmitAndPattern(AndPatternNode andPattern)
    {
        var left = EmitUnaryPattern(andPattern.Left);

        if (andPattern.Right is null || ReferenceEquals(andPattern.Right, andPattern.Left))
            return left;

        var right = EmitUnaryPattern(andPattern.Right);
        return $"{left} and {right}";
    }

    private string EmitUnaryPattern(UnaryPatternNode unaryPattern)
    {
        switch (unaryPattern)
        {
            case NotPatternUnaryPatternNode notPattern:
                return $"not {EmitPrimaryPattern(notPattern.Operand)}";
            case DirectPatternUnaryPatternNode directPattern:
                return EmitPrimaryPattern(directPattern.Primary);
            default:
                return "";
        }
    }

    private string EmitPrimaryPattern(PrimaryPatternNode primaryPattern)
    {
        switch (primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
            {
                var type = EmitTypeReference(typePattern.Type);
                var name = typePattern.Name.Lexeme;
                return $"{type} {name}";
            }
            case VarPatternPrimaryPatternNode varPattern:
                return $"var {varPattern.Name.Lexeme}";
            case ConstantPatternPrimaryPatternNode constantPattern:
                return EmitConstantLiteral(constantPattern.Literal);
            case LessThanRelationalPatternPrimaryPatternNode ltPattern:
                return $"< {EmitExpression(ltPattern.Constant)}";
            case GreaterThanRelationalPatternPrimaryPatternNode gtPattern:
                return $"> {EmitExpression(gtPattern.Constant)}";
            case LessThanOrEqualRelationalPatternPrimaryPatternNode ltePattern:
                return $"<= {EmitExpression(ltePattern.Constant)}";
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gtePattern:
                return $">= {EmitExpression(gtePattern.Constant)}";
            case PropertyPatternPrimaryPatternNode propertyPattern:
            {
                var sb = new StringBuilder();

                if (propertyPattern.TypeName is not null)
                {
                    sb.Append(ExtractQualifiedName(propertyPattern.TypeName));
                    sb.Append(' ');
                }

                sb.Append("{ ");
                if (propertyPattern.Subpatterns is not null)
                {
                    sb.Append(EmitPropertySubpatternList(propertyPattern.Subpatterns));
                    sb.Append(' ');
                }
                sb.Append('}');

                if (propertyPattern.Name.HasValue)
                {
                    sb.Append(' ');
                    sb.Append(propertyPattern.Name.Value.Lexeme);
                }

                return sb.ToString();
            }
            case ListPatternPrimaryPatternNode listPattern:
            {
                if (listPattern.Elements is null)
                    return "[]";

                return $"[{EmitListPatternElementList(listPattern.Elements)}]";
            }
            case ParenthesizedPatternPrimaryPatternNode parenPattern:
                return $"({EmitPattern(parenPattern.Inner)})";
            default:
                return "";
        }
    }

    private static string EmitConstantLiteral(ConstantLiteralNode literal)
    {
        return literal switch
        {
            IntegerConstantConstantLiteralNode intConst => intConst.Value.Lexeme,
            FloatConstantConstantLiteralNode floatConst => floatConst.Value.Lexeme,
            StringConstantConstantLiteralNode strConst => strConst.Value.Lexeme,
            CharConstantConstantLiteralNode charConst => charConst.Value.Lexeme,
            TrueConstantConstantLiteralNode => "true",
            FalseConstantConstantLiteralNode => "false",
            _ => "",
        };
    }

    private string EmitPropertySubpatternList(PropertySubpatternListNode subpatternList)
    {
        var parts = new List<string>();
        parts.Add(EmitPropertySubpattern(subpatternList.PropertySubpattern));

        foreach (var subpattern in subpatternList.PropertySubpatterns)
        {
            parts.Add(EmitPropertySubpattern(subpattern));
        }

        return string.Join(", ", parts);
    }

    private string EmitPropertySubpattern(PropertySubpatternNode subpattern)
    {
        var name = subpattern.Name.Lexeme;
        var pattern = EmitPattern(subpattern.Pattern);
        return $"{name}: {pattern}";
    }

    private string EmitListPatternElementList(ListPatternElementListNode elementList)
    {
        var parts = new List<string>();
        parts.Add(EmitListPatternElement(elementList.ListPatternElement));

        foreach (var element in elementList.ListPatternElements)
        {
            parts.Add(EmitListPatternElement(element));
        }

        return string.Join(", ", parts);
    }

    private string EmitListPatternElement(ListPatternElementNode element)
    {
        switch (element)
        {
            case PatternElementListPatternElementNode patternElement:
                return EmitPattern(patternElement.Pattern);
            case SliceElementListPatternElementNode sliceElement:
            {
                if (sliceElement.Name.HasValue)
                    return $".. {sliceElement.Name.Value.Lexeme}";
                return "..";
            }
            default:
                return "";
        }
    }

    private string EmitAdditive(AdditiveNode additive)
    {
        var left = EmitMultiplicative(additive.Left);

        if (additive.Right is not null && !ReferenceEquals(additive.Right, additive.Left))
        {
            var op = additive.Operator.Lexeme;
            var right = EmitMultiplicative(additive.Right);
            return $"{left} {op} {right}";
        }

        return left;
    }

    private string EmitMultiplicative(MultiplicativeNode multiplicative)
    {
        var left = EmitUnary(multiplicative.Left);

        if (multiplicative.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left))
        {
            var op = multiplicative.Operator.Lexeme;
            var right = EmitUnary(multiplicative.Right);
            return $"{left} {op} {right}";
        }

        return left;
    }

    private string EmitUnary(UnaryNode unary)
    {
        switch (unary)
        {
            case CastExpressionUnaryNode castExpr:
                return $"({EmitTypeReference(castExpr.TargetType)}){EmitUnary(castExpr.Operand)}";
            case LogicalNotUnaryNode logicalNot:
                return $"!{EmitUnary(logicalNot.Operand)}";
            case NegateUnaryNode negate:
                return $"-{EmitUnary(negate.Operand)}";
            case PostfixExpressionUnaryNode postfix:
                return EmitPostfix(postfix.Primary);
            default:
                return "";
        }
    }

    private string EmitPostfix(PostfixNode postfix)
    {
        var result = EmitPrimary(postfix.Target);

        foreach (var suffix in postfix.Suffix)
        {
            result = EmitPostfixSuffix(result, suffix);
        }

        return result;
    }

    private string EmitPostfixSuffix(string target, PostfixSuffixNode suffix)
    {
        switch (suffix)
        {
            case MethodCallSuffixPostfixSuffixNode methodCall:
            {
                var name = methodCall.Name.Lexeme;
                var typeArgs = EmitTypeArgumentList(methodCall.TypeArgs);
                var args = EmitArgumentList(methodCall.Arguments);
                return $"{target}.{name}{typeArgs}({args})";
            }
            case MemberAccessSuffixPostfixSuffixNode memberAccess:
            {
                var name = memberAccess.Name.Lexeme;
                return $"{target}.{name}";
            }
            case IndexSuffixPostfixSuffixNode indexSuffix:
            {
                var index = EmitExpression(indexSuffix.Index);
                return $"{target}[{index}]";
            }
            case InvocationSuffixPostfixSuffixNode invocation:
            {
                var args = EmitArgumentList(invocation.Arguments);
                return $"{target}({args})";
            }
            default:
                return target;
        }
    }

    private string EmitPrimary(PrimaryNode primary)
    {
        switch (primary)
        {
            case IntegerLiteralExpressionPrimaryNode intLit:
                return intLit.IntegerLiteral.Lexeme;
            case HexLiteralExpressionPrimaryNode hexLit:
                return hexLit.HexLiteral.Lexeme;
            case BinaryLiteralExpressionPrimaryNode binLit:
                return binLit.BinaryLiteral.Lexeme;
            case FloatLiteralExpressionPrimaryNode floatLit:
                return floatLit.FloatLiteral.Lexeme;
            case StringLiteralExpressionPrimaryNode stringLit:
                return stringLit.StringLiteral.Lexeme;
            case InterpolatedStringExpressionPrimaryNode interpolated:
                return EmitInterpolatedString(interpolated.InterpolatedString);
            case CharLiteralExpressionPrimaryNode charLit:
                return charLit.CharLiteral.Lexeme;
            case TrueLiteralExpressionPrimaryNode:
                return "true";
            case FalseLiteralExpressionPrimaryNode:
                return "false";
            case ThisExpressionPrimaryNode:
                return "this";
            case ArrayCreationExpressionPrimaryNode arrayCreation:
            {
                var type = EmitTypeReference(arrayCreation.Type);
                var size = EmitExpression(arrayCreation.Size);
                return $"new {type}[{size}]";
            }
            case ObjectCreationExpressionPrimaryNode objCreation:
            {
                var type = ExtractQualifiedName(objCreation.Type);
                var typeArgs = EmitTypeArgumentList(objCreation.TypeArgs);
                var args = EmitArgumentList(objCreation.Arguments);
                return $"new {type}{typeArgs}({args})";
            }
            case ParenthesizedExpressionPrimaryNode paren:
                return $"({EmitExpression(paren.Inner)})";
            case IdentifierExpressionPrimaryNode ident:
                return ident.Name.Lexeme;
            case FieldKeywordExpressionPrimaryNode:
                return "field";
            case TypeLiteralExpressionPrimaryNode typeLiteral:
                return EmitTypeReference(typeLiteral.TypeRef);
            default:
                return "";
        }
    }

    private string EmitInterpolatedString(InterpolatedStringNode interpolated)
    {
        var sb = new StringBuilder();
        sb.Append("$\"");

        foreach (var part in interpolated.Parts)
        {
            switch (part)
            {
                case TextPartInterpolatedPartNode textPart:
                    sb.Append(textPart.Text.Lexeme);
                    break;
                case ExpressionPartInterpolatedPartNode exprPart:
                    sb.Append('{');
                    sb.Append(EmitExpression(exprPart.Expression));
                    sb.Append('}');
                    break;
            }
        }

        sb.Append('"');
        return sb.ToString();
    }

    // ─── Modifier Emission ───────────────────────────────────────────────

    private static string EmitModifiers(ImmutableArray<ModifierNode> modifiers)
    {
        if (modifiers.IsEmpty) return "";

        var parts = new List<string>();
        foreach (var mod in modifiers)
        {
            switch (mod)
            {
                case PublicModifierModifierNode:
                    parts.Add("public");
                    break;
                case PrivateModifierModifierNode:
                    parts.Add("private");
                    break;
                case StaticModifierModifierNode:
                    parts.Add("static");
                    break;
                case IntrinsicModifierModifierNode:
                    parts.Add("intrinsic");
                    break;
            }
        }

        return string.Join(" ", parts) + " ";
    }

    // ─── Utilities ───────────────────────────────────────────────────────

    /// <summary>
    /// Emits a base list (base types / interface list) as a comma-separated string.
    /// Each entry is a type name with optional type arguments.
    /// </summary>
    private string EmitBaseList(BaseListNode baseList)
    {
        var parts = new List<string>();
        parts.Add(EmitBaseTypeRef(baseList.First));
        foreach (var rest in baseList.BaseTypeRefs)
        {
            parts.Add(EmitBaseTypeRef(rest));
        }
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Emits a single base type reference (name with optional type arguments).
    /// </summary>
    private string EmitBaseTypeRef(BaseTypeRefNode baseTypeRef)
    {
        return baseTypeRef.Name.Lexeme + EmitTypeArgumentList(baseTypeRef.TypeArgs);
    }

    /// <summary>
    /// Emits a type parameter list (e.g., &lt;T, U&gt;) or empty string if null.
    /// </summary>
    private static string EmitTypeParameterList(TypeParameterListNode? typeParams)
    {
        if (typeParams is null) return "";

        var parts = new List<string>();
        parts.Add(typeParams.TypeParameter.Name.Lexeme);
        foreach (var tp in typeParams.TypeParameters)
        {
            parts.Add(tp.Name.Lexeme);
        }
        return $"<{string.Join(", ", parts)}>";
    }

    /// <summary>
    /// Emits a type argument list (e.g., &lt;int, string&gt;) or empty string if null.
    /// </summary>
    private string EmitTypeArgumentList(TypeArgumentListNode? typeArgs)
    {
        if (typeArgs is null) return "";

        var parts = new List<string>();
        parts.Add(EmitTypeReference(typeArgs.TypeReference));
        foreach (var typeRef in typeArgs.TypeReferences)
        {
            parts.Add(EmitTypeReference(typeRef));
        }
        return $"<{string.Join(", ", parts)}>";
    }

    /// <summary>
    /// Extracts the qualified name from a QualifiedIdentifierNode.
    /// When source text is available, uses the Span to extract the exact text.
    /// Otherwise, reconstructs from the IDENTIFIER and IDENTIFIERs properties.
    /// </summary>
    private string ExtractQualifiedName(QualifiedIdentifierNode qualifiedId)
    {
        // Prefer extracting from source text via Span (handles dotted names correctly)
        if (_sourceText is not null)
        {
            var span = qualifiedId.Span;
            if (span.Length > 0 && span.Start + span.Length <= _sourceText.Length)
            {
                return _sourceText.Substring(span.Start, span.Length);
            }
        }

        // Fall back to reconstructing from token properties (for hand-crafted ASTs)
        var first = qualifiedId.IDENTIFIER.Lexeme;
        if (string.IsNullOrEmpty(first))
        {
            return "";
        }

        if (qualifiedId.IDENTIFIERs.IsDefaultOrEmpty)
        {
            return first;
        }

        // Reconstruct the dotted name: first.second.third...
        var sb = new StringBuilder(first);
        foreach (var token in qualifiedId.IDENTIFIERs)
        {
            if (!string.IsNullOrEmpty(token.Lexeme))
            {
                sb.Append('.');
                sb.Append(token.Lexeme);
            }
        }
        return sb.ToString();
    }

    private void AppendLine(string text = "")
    {
        if (string.IsNullOrEmpty(text))
        {
            _sb.AppendLine();
        }
        else
        {
            for (var i = 0; i < _indentLevel; i++)
            {
                _sb.Append(IndentUnit);
            }
            _sb.AppendLine(text);
        }
    }
}
