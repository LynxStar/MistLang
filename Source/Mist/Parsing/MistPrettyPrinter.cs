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
    private readonly StringBuilder sb = new();
    private int indentLevel;
    private const string IndentUnit = "    "; // 4 spaces per indent level
    private readonly string? sourceText;

    private MistPrettyPrinter(string? sourceText = null)
    {
        this.sourceText = sourceText;
    }

    /// <summary>
    /// Pretty-prints the given AST node to syntactically valid Mist source text.
    /// </summary>
    public static string Print(SyntaxNode ast)
    {
        var printer = new MistPrettyPrinter();
        printer.Visit(ast);
        return printer.sb.ToString();
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
        return printer.sb.ToString();
    }

    private void Visit(SyntaxNode node)
    {
        switch(node)
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
        foreach(var usingDecl in cu.Usings)
        {
            EmitUsingDeclaration(usingDecl);
        }

        if(cu.Usings.Length > 0 && cu.Items.Length > 0)
        {
            AppendLine();
        }

        foreach(var item in cu.Items)
        {
            switch(item)
            {
                case NamespaceItemTopLevelItemNode nsItem:
                    EmitNamespaceDeclaration(nsItem.NamespaceDeclaration);
                    break;
                case TypeItemTopLevelItemNode typeItem:
                    EmitTypeDeclaration(typeItem.TypeDeclaration);
                    break;
                case ExtensionItemTopLevelItemNode extensionItem:
                    EmitExtensionBlockDeclaration(extensionItem.ExtensionDeclaration);
                    break;
                case UsingItemTopLevelItemNode usingItem:
                    EmitUsingDeclaration(usingItem.UsingDeclaration);
                    break;
                case StatementItemTopLevelItemNode stmtItem:
                    EmitStatement(stmtItem.Statement);
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
        switch(ns)
        {
            case FileScopedNamespaceNamespaceDeclarationNode fileScopedNs:
                {
                    var name = ExtractQualifiedName(fileScopedNs.Name);
                    AppendLine($"namespace {name};");
                    AppendLine();

                    foreach(var bodyItem in fileScopedNs.Body)
                    {
                        EmitNamespaceBodyItem(bodyItem);
                    }

                    break;
                }
            case BlockNamespaceNamespaceDeclarationNode blockNs:
                {
                    var name = ExtractQualifiedName(blockNs.Name);
                    AppendLine($"namespace {name} {{");
                    indentLevel++;

                    foreach(var bodyItem in blockNs.Body)
                    {
                        EmitNamespaceBodyItem(bodyItem);
                    }

                    indentLevel--;
                    AppendLine("}");
                    break;
                }
        }
    }

    private void EmitNamespaceBodyItem(NamespaceBodyItemNode bodyItem)
    {
        switch(bodyItem)
        {
            case NamespaceTypeItemNamespaceBodyItemNode typeItem:
                EmitTypeDeclaration(typeItem.TypeDeclaration);
                break;
            case NamespaceExtensionItemNamespaceBodyItemNode extensionItem:
                EmitExtensionBlockDeclaration(extensionItem.ExtensionDeclaration);
                break;
            case NamespaceNamespaceItemNamespaceBodyItemNode nsItem:
                EmitNamespaceDeclaration(nsItem.NamespaceDeclaration);
                break;
        }
    }

    // ─── Type Alias Declarations ────────────────────────────────────────

    private void EmitMemberTypeAliasDeclaration(MemberTypeAliasDeclarationNode aliasDecl)
    {
        var modifiers = EmitModifiers(aliasDecl.Modifiers);
        var name = aliasDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(aliasDecl.TypeParams);
        var typeExpr = EmitRefinableTypeExpression(aliasDecl.Type);
        var constraints = EmitConstraintClauses(aliasDecl.Constraints);
        AppendLine($"{modifiers}type {name}{typeParams} = {typeExpr}{constraints};");
    }

    private void EmitTypeAliasDeclaration(TypeAliasDeclarationNode aliasDecl)
    {
        var name = aliasDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(aliasDecl.TypeParams);
        var typeExpr = EmitRefinableTypeExpression(aliasDecl.Type);
        var constraints = EmitConstraintClauses(aliasDecl.Constraints);
        AppendLine($"type {name}{typeParams} = {typeExpr}{constraints};");
    }

    private string EmitRefinableTypeExpression(RefinableTypeExpressionNode node)
    {
        // Union when there are PIPE-separated continuations; otherwise a single (optionally refined) type.
        if(node.RefinableSingleTypeExprs.Length > 0)
        {
            var parts = new List<string>
            {
                EmitRefinableSingle(node.First)
            };
            foreach(var constituent in node.RefinableSingleTypeExprs)
            {
                parts.Add(EmitRefinableSingle(constituent));
            }

            return string.Join(" | ", parts);
        }

        return EmitRefinableSingle(node.First);
    }

    private string EmitRefinableSingle(RefinableSingleTypeExprNode expr)
    {
        var type = EmitSingleTypeRef(expr.Type);
        if(expr.Refinement is null)
        {
            return type;
        }

        var predicate = EmitRefinementPredicate(expr.Refinement);
        return $"{type} {predicate}";
    }

    // ─── Newtype Declarations ───────────────────────────────────────────

    private void EmitNewtypeDeclaration(NewtypeDeclarationNode newtypeDecl)
    {
        var name = newtypeDecl.Name.Lexeme;
        var sourceType = EmitTypeReference(newtypeDecl.SourceType);
        AppendLine($"type {name} = new {sourceType};");
    }

    // ─── Type Declarations ───────────────────────────────────────────────

    private void EmitTypeDeclaration(TypeDeclarationNode typeDecl)
    {
        switch(typeDecl)
        {
            case BodyTypeTypeDeclarationNode bodyType:
                EmitTypeBodyDeclaration(bodyType.TypeBody);
                break;
            case EnumTypeTypeDeclarationNode enumType:
                EmitEnumDeclaration(enumType.EnumDeclaration);
                break;
            case NewtypeTypeTypeDeclarationNode newtypeType:
                EmitNewtypeDeclaration(newtypeType.NewtypeDeclaration);
                break;
            case TypeAliasTypeTypeDeclarationNode typeAliasType:
                EmitTypeAliasDeclaration(typeAliasType.TypeAliasDeclaration);
                break;
        }
    }

    private void EmitTypeBodyDeclaration(TypeBodyDeclarationNode typeBody)
    {
        var modifiers = EmitModifiers(typeBody.Modifiers);
        var keyword = typeBody.Keyword switch
        {
            ClassKeywordTypeKeywordNode => "class",
            StructKeywordTypeKeywordNode => "struct",
            AbstractKeywordTypeKeywordNode => "abstract",
            _ => throw new InvalidOperationException($"Unknown type keyword: {typeBody.Keyword.GetType().Name}"),
        };
        var name = typeBody.Name.Lexeme;
        var typeParams = EmitTypeParameterList(typeBody.TypeParams);
        var baseClause = "";
        if(typeBody.BaseList is not null)
        {
            baseClause = $" : {EmitBaseList(typeBody.BaseList)}";
        }

        var constraints = EmitConstraintClauses(typeBody.Constraints);
        AppendLine($"{modifiers}{keyword} {name}{typeParams}{baseClause}{constraints} {{");
        indentLevel++;

        EmitMembers(typeBody.Members);

        indentLevel--;
        AppendLine("}");
    }

    private void EmitMembers(ImmutableArray<MemberDeclarationNode> members)
    {
        for(var i = 0; i < members.Length; i++)
        {
            if(i > 0)
            {
                AppendLine();
            }

            EmitMemberDeclaration(members[i]);
        }
    }

    // ─── Member Declarations ─────────────────────────────────────────────

    private void EmitMemberDeclaration(MemberDeclarationNode memberDecl)
    {
        switch(memberDecl)
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
            case DefaultBlockMemberMemberDeclarationNode defaultBlockMember:
                EmitDefaultBlockDeclaration(defaultBlockMember.DefaultBlock);
                break;
            case NestedTypeMemberMemberDeclarationNode nestedTypeMember:
                EmitTypeDeclaration(nestedTypeMember.NestedType);
                break;
            case AssociatedTypeMemberMemberDeclarationNode associatedTypeMember:
                EmitAssociatedTypeSlotDeclaration(associatedTypeMember.AssociatedType);
                break;
            case UnconstrainedAssociatedTypeMemberMemberDeclarationNode unconstrainedMember:
                EmitUnconstrainedAssociatedTypeSlotDeclaration(unconstrainedMember.UnconstrainedAssociatedType);
                break;
            case MemberTypeAliasMemberMemberDeclarationNode memberTypeAlias:
                EmitMemberTypeAliasDeclaration(memberTypeAlias.MemberTypeAlias);
                break;
            case ModifiedAssociatedTypeMemberMemberDeclarationNode modifiedMember:
                EmitModifiedAssociatedTypeSlotDeclaration(modifiedMember.ModifiedAssociatedType);
                break;
        }
    }

    private void EmitFieldDeclaration(FieldDeclarationNode fieldDecl)
    {
        var modifiers = EmitModifiers(fieldDecl.Modifiers);
        var ownership = EmitOwnershipSpecifier(fieldDecl.OwnershipSpecifier);
        var type = EmitTypeReference(fieldDecl.Type);
        var name = fieldDecl.Name.Lexeme;

        if(fieldDecl.Initializer is not null)
        {
            AppendLine($"{modifiers}{ownership}{type} {name} = {EmitExpression(fieldDecl.Initializer)};");
        }
        else
        {
            AppendLine($"{modifiers}{ownership}{type} {name};");
        }
    }

    private void EmitConstructorDeclaration(ConstructorDeclarationNode ctorDecl)
    {
        var modifiers = EmitModifiers(ctorDecl.Modifiers);
        var name = ctorDecl.Name.Lexeme;
        var parameters = EmitParameterList(ctorDecl.Parameters);
        var withClauses = EmitWithClauses(ctorDecl.WithClauses);
        var constraints = EmitConstraintClauses(ctorDecl.Constraints);

        var chain = "";
        if(ctorDecl.Chain is ThisChainConstructorChainNode thisChain)
        {
            chain = " : this(" + EmitArgumentList(thisChain.Arguments) + ")";
        }
        else if(ctorDecl.Chain is BaseChainConstructorChainNode baseChain)
        {
            chain = " : base(" + EmitArgumentList(baseChain.Arguments) + ")";
        }

        AppendLine($"{modifiers}{name}({parameters}){withClauses}{constraints}{chain} {{");
        indentLevel++;

        if(ctorDecl.Body.GetBlock() is BlockNode ctorBlock)
        {
            foreach(var stmt in ctorBlock.Statements)
            {
                EmitStatement(stmt);
            }
        }

        indentLevel--;
        AppendLine("}");
    }

    private void EmitMethodDeclaration(MethodDeclarationNode methodDecl)
    {
        var modifiers = EmitModifiers(methodDecl.Modifiers);
        var returnOwnership = EmitOwnershipSpecifier(methodDecl.ReturnOwnership);
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(methodDecl.TypeParams);
        var parameters = EmitParameterList(methodDecl.Parameters);
        var withClauses = EmitWithClauses(methodDecl.WithClauses);
        var constraints = EmitConstraintClauses(methodDecl.Constraints);

        if(methodDecl.Body is MethodNoBodyMethodBodyNode)
        {
            AppendLine($"{modifiers}{returnOwnership}{returnType} {name}{typeParams}({parameters}){withClauses}{constraints};");
            return;
        }

        AppendLine($"{modifiers}{returnOwnership}{returnType} {name}{typeParams}({parameters}){withClauses}{constraints} {{");
        indentLevel++;

        if(methodDecl.Body.GetBlock() is BlockNode methodBlock)
        {
            foreach(var stmt in methodBlock.Statements)
            {
                EmitStatement(stmt);
            }
        }

        indentLevel--;
        AppendLine("}");
    }

    // ─── Property Declarations ──────────────────────────────────────────

    private void EmitPropertyDeclaration(PropertyDeclarationNode propertyDecl)
    {
        switch(propertyDecl)
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
        foreach(var accessor in blockProp.Accessors)
        {
            accessorParts.Add(EmitAccessorDeclaration(accessor));
        }

        var accessorBody = string.Join(" ", accessorParts);

        // Emit initializer if present.
        if(blockProp.Initializer is not null)
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
        switch(accessor)
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
        if(modifier is null)
        {
            return "";
        }

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
        if(block.Statements.IsEmpty)
        {
            return "{ }";
        }

        var parts = new List<string>();
        foreach(var stmt in block.Statements)
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
        switch(stmt)
        {
            case ReturnStatementStatementNode returnStmt:
                {
                    if(returnStmt.ReturnStatement.Value is not null)
                    {
                        return $"return {EmitExpression(returnStmt.ReturnStatement.Value)};";
                    }

                    return "return;";
                }
            case ExpressionStatementStatementNode exprStmt:
                return $"{EmitExpression(exprStmt.ExpressionStatement.Expression)};";
            case VariableDeclarationStatementStatementNode varDeclStmt:
                {
                    switch(varDeclStmt.VariableDeclaration)
                    {
                        case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                            {
                                var readonlyPrefix = typed.ReadonlyModifier is not null ? "readonly " : "";
                                var type = EmitTypeReference(typed.Type);
                                var name = typed.Name.Lexeme;
                                var init = EmitExpression(typed.Initializer);
                                return $"{readonlyPrefix}{type} {name} = {init};";
                            }
                        case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                            {
                                var readonlyPrefix = inferred.ReadonlyModifier is not null ? "readonly " : "";
                                var name = inferred.Name.Lexeme;
                                var init = EmitExpression(inferred.Initializer);
                                return $"{readonlyPrefix}var {name} = {init};";
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
        var modifiers = EmitModifiers(indexerDecl.Modifiers);
        var type = EmitTypeReference(indexerDecl.Type);
        var parameters = EmitParameterList(indexerDecl.Parameters);
        var constraints = EmitConstraintClauses(indexerDecl.Constraints);

        if(indexerDecl.Body is BlockIndexerBodyIndexerBodyNode blockBody)
        {
            // Build accessor list.
            var accessorParts = new List<string>();
            foreach(var accessor in blockBody.Accessors)
            {
                accessorParts.Add(EmitAccessorDeclaration(accessor));
            }

            var accessorBody = string.Join(" ", accessorParts);
            AppendLine($"{modifiers}{type} this[{parameters}]{constraints} {{ {accessorBody} }}");
        }
        else
        {
            // Intrinsic indexer — semicolon-terminated.
            AppendLine($"{modifiers}{type} this[{parameters}]{constraints};");
        }
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

        if(opDecl.Body is OperatorBlockBodyOperatorBodyNode blockBody)
        {
            AppendLine($"{modifiers}{returnType} operator {symbol}({parameters}) {{");
            indentLevel++;

            foreach(var stmt in blockBody.Block.Statements)
            {
                EmitStatement(stmt);
            }

            indentLevel--;
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

        if(castOpDecl.Body is OperatorBlockBodyOperatorBodyNode blockBody)
        {
            AppendLine($"{modifiers}{castKind} operator {targetType}({parameters}) {{");
            indentLevel++;

            foreach(var stmt in blockBody.Block.Statements)
            {
                EmitStatement(stmt);
            }

            indentLevel--;
            AppendLine("}");
        }
        else
        {
            AppendLine($"{modifiers}{castKind} operator {targetType}({parameters});");
        }
    }

    // ─── Default Block Declarations ─────────────────────────────────────

    private void EmitDefaultBlockDeclaration(DefaultBlockDeclarationNode defaultBlock)
    {
        if(defaultBlock.Entries.IsEmpty)
        {
            AppendLine("default { }");
            return;
        }

        AppendLine("default {");
        indentLevel++;

        foreach(var entry in defaultBlock.Entries)
        {
            switch(entry)
            {
                case DefaultFieldOverrideDefaultFieldInitializerNode fieldOverride:
                    {
                        var fieldName = fieldOverride.FieldName.Lexeme;
                        var value = EmitExpression(fieldOverride.Value);
                        AppendLine($"{fieldName} = {value}");
                        break;
                    }
                case DefaultTypeBindingDefaultFieldInitializerNode typeBinding:
                    {
                        var fieldName = typeBinding.FieldName.Lexeme;
                        var typeName = EmitTypeReference(typeBinding.TypeName);

                        if(typeBinding.NestedEntries.IsDefaultOrEmpty)
                        {
                            AppendLine($"{fieldName}: {typeName}");
                        }
                        else
                        {
                            var overrides = new List<string>();
                            foreach(var nested in typeBinding.NestedEntries)
                            {
                                var nestedFieldName = nested.FieldName.Lexeme;
                                var nestedValue = EmitExpression(nested.Value);
                                overrides.Add($"{nestedFieldName} = {nestedValue}");
                            }

                            AppendLine($"{fieldName}: {typeName} {{ {string.Join(", ", overrides)} }}");
                        }

                        break;
                    }
            }
        }

        indentLevel--;
        AppendLine("}");
    }

    // ─── Associated Type Slot Declarations ──────────────────────────────

    private void EmitAssociatedTypeSlotDeclaration(AssociatedTypeSlotDeclarationNode slotDecl)
    {
        var name = slotDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(slotDecl.TypeParams);
        var constraints = EmitConstraintList(slotDecl.Constraints);
        var whereClauses = EmitConstraintClauses(slotDecl.SlotWhereConstraints);
        AppendLine($"type {name}{typeParams} : {constraints}{whereClauses};");
    }

    private void EmitUnconstrainedAssociatedTypeSlotDeclaration(UnconstrainedAssociatedTypeSlotDeclarationNode slotDecl)
    {
        var name = slotDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(slotDecl.TypeParams);
        AppendLine($"type {name}{typeParams};");
    }

    private void EmitModifiedAssociatedTypeSlotDeclaration(ModifiedAssociatedTypeSlotDeclarationNode slotDecl)
    {
        var modifiers = EmitModifiers(slotDecl.Modifiers);
        var name = slotDecl.Name.Lexeme;
        var typeParams = EmitTypeParameterList(slotDecl.TypeParams);
        if(slotDecl.Constraints is not null)
        {
            var constraints = EmitConstraintList(slotDecl.Constraints);
            var whereClauses = EmitConstraintClauses(slotDecl.SlotWhereConstraints);
            AppendLine($"{modifiers}type {name}{typeParams} : {constraints}{whereClauses};");
        }
        else
        {
            AppendLine($"{modifiers}type {name}{typeParams};");
        }
    }

    // ─── Enum Declarations ──────────────────────────────────────────────

    private void EmitEnumDeclaration(EnumDeclarationNode enumDecl)
    {
        var modifiers = EmitModifiers(enumDecl.Modifiers);
        var name = enumDecl.Name.Lexeme;

        var typeAnnotation = "";
        if(enumDecl.TypeAnnotation is not null)
        {
            typeAnnotation = $" : {EmitTypeReference(enumDecl.TypeAnnotation)}";
        }

        if(enumDecl.Members is null)
        {
            AppendLine($"{modifiers}enum {name}{typeAnnotation} {{");
            AppendLine("}");
            return;
        }

        AppendLine($"{modifiers}enum {name}{typeAnnotation} {{");
        indentLevel++;

        EmitEnumMemberList(enumDecl.Members);

        indentLevel--;
        AppendLine("}");
    }

    private void EmitEnumMemberList(EnumMemberListNode memberList)
    {
        var members = new List<EnumMemberNode> { memberList.EnumMember };
        foreach(var member in memberList.EnumMembers)
        {
            members.Add(member);
        }

        var hasTrailingComma = memberList.COMMA.HasValue;

        for(var i = 0; i < members.Count; i++)
        {
            var member = members[i];
            var memberText = EmitEnumMember(member);

            var isLast = i == members.Count - 1;
            if(!isLast)
            {
                AppendLine($"{memberText},");
            }
            else if(hasTrailingComma)
            {
                AppendLine($"{memberText},");
            }
            else
            {
                AppendLine(memberText);
            }
        }
    }

    private string EmitEnumMember(EnumMemberNode member)
    {
        var prefix = member.IsDefault is not null ? "default " : "";
        var name = member.Name.Lexeme;

        if(member.Value is not null)
        {
            return $"{prefix}{name} = {EmitExpression(member.Value)}";
        }

        return $"{prefix}{name}";
    }

    // ─── Extension Block Declarations ────────────────────────────────────

    private void EmitExtensionBlockDeclaration(ExtensionBlockDeclarationNode extDecl)
    {
        var typeParams = EmitTypeParameterList(extDecl.TypeParams);
        var receiverType = EmitTypeReference(extDecl.ReceiverType);
        var receiverName = extDecl.ReceiverName.Lexeme;

        // Grammar: TypeParameterList? EXTENSION ( ... ) — type params precede the keyword
        var prefix = typeParams.Length > 0 ? $"{typeParams} " : "";
        AppendLine($"{prefix}extension({receiverType} {receiverName}) {{");
        indentLevel++;

        for(var i = 0; i < extDecl.Members.Length; i++)
        {
            if(i > 0)
            {
                AppendLine();
            }

            EmitExtensionMemberDeclaration(extDecl.Members[i]);
        }

        indentLevel--;
        AppendLine("}");
    }

    private void EmitExtensionMemberDeclaration(ExtensionMemberDeclarationNode member)
    {
        switch(member)
        {
            case ExtensionMethodMemberExtensionMemberDeclarationNode methodMember:
                EmitMethodDeclaration(methodMember.Method);
                break;
            case ExtensionPropertyMemberExtensionMemberDeclarationNode propertyMember:
                EmitPropertyDeclaration(propertyMember.Property);
                break;
        }
    }

    // ─── Parameters and Arguments ────────────────────────────────────────

    private string EmitParameterList(ParameterListNode? paramList)
    {
        if(paramList is null)
        {
            return "";
        }

        var parameters = new List<string>
        {
            EmitParameter(paramList.Parameter)
        };

        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode param && !ReferenceEquals(param, paramList.Parameter))
            {
                parameters.Add(EmitParameter(param));
            }
        }

        return string.Join(", ", parameters);
    }

    private string EmitParameter(ParameterNode param)
    {
        var prefix = param.Modifier is not null ? "readonly " : "";
        if(param.RefModifier is not null)
        {
            prefix += "ref ";
        }

        prefix += EmitOwnershipSpecifier(param.OwnershipSpecifier);
        var type = EmitTypeReference(param.Type);
        var name = param.Name.Lexeme;
        return $"{prefix}{type} {name}";
    }

    private static string EmitOwnershipSpecifier(OwnershipSpecifierNode? specifier)
    {
        return specifier switch
        {
            OwnedSpecifierOwnershipSpecifierNode => "owned ",
            FrozenSpecifierOwnershipSpecifierNode => "frozen ",
            ReadonlyRefSpecifierOwnershipSpecifierNode => "readonly ref ",
            BorrowedSpecifierOwnershipSpecifierNode => "borrowed ",
            RefSpecifierOwnershipSpecifierNode => "ref ",
            _ => ""
        };
    }

    private string EmitArgumentList(ArgumentListNode? argList)
    {
        if(argList is null)
        {
            return "";
        }

        var arguments = new List<string>
        {
            EmitArgument(argList.Argument)
        };

        foreach(var arg in argList.Arguments)
        {
            arguments.Add(EmitArgument(arg));
        }

        return string.Join(", ", arguments);
    }

    private string EmitArgument(ArgumentNode arg)
    {
        return arg switch
        {
            RefArgumentArgumentNode refArg => $"ref {EmitExpression(refArg.Expression)}",
            ValueArgumentArgumentNode valueArg => EmitExpression(valueArg.Expression),
            _ => EmitExpression(ArgumentListNodeExtensions.ExtractExpression(arg))
        };
    }

    // ─── Type References ─────────────────────────────────────────────────

    private string EmitElementTypeRef(ElementTypeRefNode elementTypeRef)
    {
        return elementTypeRef switch
        {
            PrimitiveElementTypeRefElementTypeRefNode primitiveRef => EmitPrimitiveType(primitiveRef.PrimitiveType),
            UnknownElementTypeRefElementTypeRefNode => "unknown",
            NamedElementTypeRefElementTypeRefNode namedRef => ExtractQualifiedName(namedRef.Name) + EmitTypeArgumentList(namedRef.TypeArgs),
            _ => "object"
        };
    }

    private string EmitTypeReference(TypeReferenceNode typeRef)
    {
        // Union (non-empty PIPE-separated continuation list): emit constituents
        // joined by " | ". A single (non-union) type ref delegates to EmitSingleTypeRef.
        if(typeRef.SingleTypeRefs.Length > 0)
        {
            var parts = new List<string>
            {
                EmitSingleTypeRef(typeRef.First)
            };
            foreach(var constituent in typeRef.SingleTypeRefs)
            {
                parts.Add(EmitSingleTypeRef(constituent));
            }

            return string.Join(" | ", parts);
        }

        return EmitSingleTypeRef(typeRef.First);
    }

    private string EmitSingleTypeRef(SingleTypeRefNode singleTypeRef)
    {
        switch(singleTypeRef)
        {
            case PrimitiveSingleTypeRefSingleTypeRefNode primitiveRef:
                {
                    var keyword = EmitPrimitiveType(primitiveRef.PrimitiveType);
                    if(primitiveRef.ArrayRank is SizedArrayRankArrayRankSuffixNode sizedRank)
                    {
                        return $"{keyword}[{sizedRank.Size.Lexeme}]";
                    }
                    else if(primitiveRef.ArrayRank is not null)
                    {
                        return $"{keyword}[]";
                    }

                    return keyword;
                }
            case VoidSingleTypeRefSingleTypeRefNode:
                return "void";
            case UnknownSingleTypeRefSingleTypeRefNode:
                return "unknown";
            case NamedSingleTypeRefSingleTypeRefNode namedRef:
                {
                    var name = ExtractQualifiedName(namedRef.Name);
                    var typeArgs = EmitTypeArgumentList(namedRef.TypeArgs);
                    if(namedRef.ArrayRank is SizedArrayRankArrayRankSuffixNode sizedRank)
                    {
                        return $"{name}{typeArgs}[{sizedRank.Size.Lexeme}]";
                    }
                    else if(namedRef.ArrayRank is not null)
                    {
                        return $"{name}{typeArgs}[]";
                    }

                    return $"{name}{typeArgs}";
                }
            case ParenthesizedTypeRefSingleTypeRefNode parenRef:
                {
                    var inner = EmitTypeReference(parenRef.Inner);
                    if(parenRef.ArrayRank is SizedArrayRankArrayRankSuffixNode sizedRank)
                    {
                        return $"({inner})[{sizedRank.Size.Lexeme}]";
                    }
                    else if(parenRef.ArrayRank is not null)
                    {
                        return $"({inner})[]";
                    }

                    return $"({inner})";
                }
            case TupleSingleTypeRefSingleTypeRefNode tupleRef:
                {
                    var parts = new List<string>();

                    string formatTupleElement(TupleTypeElementNode element)
                    {
                        var type = EmitTypeReference(element.Type);
                        return element.Name.HasValue ? $"{type} {element.Name.Value.Lexeme}" : type;
                    }

                    parts.Add(formatTupleElement(tupleRef.First));
                    parts.Add(formatTupleElement(tupleRef.Rest));
                    foreach(var additional in tupleRef.TupleTypeElements)
                    {
                        parts.Add(formatTupleElement(additional));
                    }

                    return $"({string.Join(", ", parts)})";
                }
            case FuncSingleTypeRefSingleTypeRefNode funcRef:
                {
                    // Emit the lowercase `func` keyword form with its type arguments
                    // (e.g., func<int, string>). The last type argument is the return type.
                    var typeArgs = EmitTypeArgumentList(funcRef.TypeArgs);
                    return $"func{typeArgs}";
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
            RegexTypePrimitiveTypeNode => "regex",
            _ => "object",
        };
    }

    // ─── Statements ──────────────────────────────────────────────────────

    private void EmitStatement(StatementNode stmt)
    {
        switch(stmt)
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
            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                AppendLine("readonly {");
                indentLevel++;
                foreach(var bodyStmt in readonlyBlockStmt.ReadonlyBlock.Body.Statements)
                {
                    EmitStatement(bodyStmt);
                }

                indentLevel--;
                AppendLine("}");
                break;
            case BlockStatementStatementNode blockStmt:
                AppendLine("{");
                indentLevel++;
                foreach(var bodyStmt in blockStmt.BlockStatement.Statements)
                {
                    EmitStatement(bodyStmt);
                }

                indentLevel--;
                AppendLine("}");
                break;
            case WithVarDeclarationStatementStatementNode withVarStmt:
                EmitWithVarDeclarationStatement(withVarStmt.WithVarDeclaration);
                break;
            case WithTypedDeclarationStatementStatementNode withTypedStmt:
                EmitWithTypedDeclarationStatement(withTypedStmt.WithTypedDeclaration);
                break;
            case WithDiscardDeclarationStatementStatementNode withDiscardStmt:
                EmitWithDiscardDeclarationStatement(withDiscardStmt.WithDiscardDeclaration);
                break;
            case WithPushStatementStatementNode withPushStmt:
                EmitWithPushStatement(withPushStmt.WithPushStatement);
                break;
            case WithBlockStatementStatementNode withBlockStmt:
                EmitWithBlockStatement(withBlockStmt.WithBlockStatement);
                break;
            case ExpressionStatementStatementNode exprStmt:
                AppendLine($"{EmitExpression(exprStmt.ExpressionStatement.Expression)};");
                break;
        }
    }

    // ─── Context Statement Declarations ─────────────────────────────────

    private void EmitWithVarDeclarationStatement(WithVarDeclarationStatementNode withVarDecl)
    {
        var name = withVarDecl.Name.Lexeme;
        var init = EmitExpression(withVarDecl.Initializer);
        AppendLine($"with var {name} = {init};");
    }

    private void EmitWithTypedDeclarationStatement(WithTypedDeclarationStatementNode withTypedDecl)
    {
        var type = EmitTypeReference(withTypedDecl.Type);
        var name = withTypedDecl.Name.Lexeme;
        var init = EmitExpression(withTypedDecl.Initializer);
        AppendLine($"with {type} {name} = {init};");
    }

    private void EmitWithDiscardDeclarationStatement(WithDiscardDeclarationStatementNode withDiscardDecl)
    {
        var type = EmitTypeReference(withDiscardDecl.Type);
        var init = EmitExpression(withDiscardDecl.Initializer);
        AppendLine($"with {type} _ = {init};");
    }

    private void EmitWithPushStatement(WithPushStatementNode withPushStmt)
    {
        var value = EmitExpression(withPushStmt.Value);
        AppendLine($"with {value};");
    }

    private void EmitWithBlockStatement(WithBlockStatementNode withBlockStmt)
    {
        var expr = EmitExpression(withBlockStmt.Expression);
        AppendLine($"with ({expr}) {{");
        indentLevel++;
        foreach(var bodyStmt in withBlockStmt.Body.Statements)
        {
            EmitStatement(bodyStmt);
        }

        indentLevel--;
        AppendLine("}");
    }

    // ─── With Clause Emission (Method Signatures) ───────────────────────

    private string EmitWithClauses(ImmutableArray<WithClauseNode> withClauses)
    {
        if(withClauses.IsDefaultOrEmpty)
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach(var clause in withClauses)
        {
            switch(clause)
            {
                case ThisTierWithClauseWithClauseNode tierClause:
                    _ = sb.Append(" with ");
                    _ = sb.Append(EmitOwnershipSpecifier(tierClause.Tier));
                    break;
                case ContextWithClauseWithClauseNode contextClause:
                    _ = sb.Append(" with ");
                    _ = sb.Append(EmitTypeReference(contextClause.Type));
                    _ = sb.Append(' ');
                    _ = sb.Append(contextClause.Name.Lexeme);
                    break;
            }
        }

        return sb.ToString();
    }

    private void EmitVariableDeclaration(VariableDeclarationStatementNode varDecl)
    {
        switch(varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                {
                    var readonlyPrefix = typed.ReadonlyModifier is not null ? "readonly " : "";
                    var ownership = EmitOwnershipSpecifier(typed.OwnershipSpecifier);
                    var type = EmitTypeReference(typed.Type);
                    var name = typed.Name.Lexeme;
                    var init = EmitExpression(typed.Initializer);
                    AppendLine($"{readonlyPrefix}{ownership}{type} {name} = {init};");
                    break;
                }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                {
                    var readonlyPrefix = inferred.ReadonlyModifier is not null ? "readonly " : "";
                    var ownership = EmitOwnershipSpecifier(inferred.OwnershipSpecifier);
                    var name = inferred.Name.Lexeme;
                    var init = EmitExpression(inferred.Initializer);
                    AppendLine($"{readonlyPrefix}{ownership}var {name} = {init};");
                    break;
                }
            case VarTupleDeconstructionVariableDeclarationStatementNode varTuple:
                {
                    var readonlyPrefix = varTuple.ReadonlyModifier is not null ? "readonly " : "";
                    var names = new List<string> { varTuple.FirstName.Lexeme };
                    foreach(var restName in varTuple.RestNames)
                    {
                        names.Add(restName.Lexeme);
                    }

                    var init = EmitExpression(varTuple.Initializer);
                    AppendLine($"{readonlyPrefix}var ({string.Join(", ", names)}) = {init};");
                    break;
                }
            case TypedTupleDeconstructionVariableDeclarationStatementNode typedTuple:
                {
                    var readonlyPrefix = typedTuple.ReadonlyModifier is not null ? "readonly " : "";
                    var parts = new List<string>();

                    string formatDeconstructionElement(TupleDeconstructionElementNode element)
                    {
                        var type = EmitTypeReference(element.Type);
                        return $"{type} {element.Name.Lexeme}";
                    }

                    parts.Add(formatDeconstructionElement(typedTuple.First));
                    parts.Add(formatDeconstructionElement(typedTuple.Rest));
                    foreach(var additional in typedTuple.TupleDeconstructionElements)
                    {
                        parts.Add(formatDeconstructionElement(additional));
                    }

                    var init = EmitExpression(typedTuple.Initializer);
                    AppendLine($"{readonlyPrefix}({string.Join(", ", parts)}) = {init};");
                    break;
                }
        }
    }

    private void EmitReturnStatement(ReturnStatementNode returnStmt)
    {
        if(returnStmt.Value is not null)
        {
            var refPrefix = returnStmt.RefKeyword is not null ? "ref " : "";
            AppendLine($"return {refPrefix}{EmitExpression(returnStmt.Value)};");
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
        indentLevel++;

        foreach(var stmt in ifStmt.ThenBranch.Statements)
        {
            EmitStatement(stmt);
        }

        indentLevel--;

        if(ifStmt.ElseBranch is not null)
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
        switch(elseClause)
        {
            case ElseIfElseClauseNode elseIf:
                {
                    var condition = EmitExpression(elseIf.ElseIf.Condition);
                    AppendLine($"}} else if ({condition}) {{");
                    indentLevel++;

                    foreach(var stmt in elseIf.ElseIf.ThenBranch.Statements)
                    {
                        EmitStatement(stmt);
                    }

                    indentLevel--;

                    if(elseIf.ElseIf.ElseBranch is not null)
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
                    indentLevel++;

                    foreach(var stmt in elseBlock.ElseBlock.Statements)
                    {
                        EmitStatement(stmt);
                    }

                    indentLevel--;
                    AppendLine("}");
                    break;
                }
        }
    }

    private void EmitWhileStatement(WhileStatementNode whileStmt)
    {
        var condition = EmitExpression(whileStmt.Condition);
        AppendLine($"while ({condition}) {{");
        indentLevel++;

        foreach(var stmt in whileStmt.Body.Statements)
        {
            EmitStatement(stmt);
        }

        indentLevel--;
        AppendLine("}");
    }

    private void EmitForStatement(ForStatementNode forStmt)
    {
        var init = EmitForInitializer(forStmt.Initializer);
        var condition = forStmt.Condition is not null ? EmitExpression(forStmt.Condition) : "";
        var iterator = forStmt.Iterator is not null ? EmitExpression(forStmt.Iterator) : "";

        AppendLine($"for ({init}; {condition}; {iterator}) {{");
        indentLevel++;

        foreach(var stmt in forStmt.Body.Statements)
        {
            EmitStatement(stmt);
        }

        indentLevel--;
        AppendLine("}");
    }

    private string EmitForInitializer(ForInitializerNode? initializer)
    {
        if(initializer is null)
        {
            return "";
        }

        switch(initializer)
        {
            case ForInitializerVariableDeclarationForInitializerNode varDecl:
                {
                    var type = EmitTypeReference(varDecl.VariableDeclaration);
                    var name = varDecl.Name.Lexeme;
                    var init = EmitExpression(varDecl.Initializer);
                    return $"{type} {name} = {init}";
                }
            case ForInitializerVarDeclarationForInitializerNode varDeclInit:
                {
                    var name = varDeclInit.Name.Lexeme;
                    var init = EmitExpression(varDeclInit.Initializer);
                    return $"var {name} = {init}";
                }
            case ForInitializerExpressionForInitializerNode exprInit:
                return EmitExpression(exprInit.Expression);
            default:
                return "";
        }
    }

    private void EmitForeachStatement(ForeachStatementNode foreachStmt)
    {
        var typeRef = foreachStmt.GetTypeOrNull();
        var type = typeRef is not null ? EmitTypeReference(typeRef) : "var";
        var name = foreachStmt.GetName().Lexeme;
        var collection = EmitExpression(foreachStmt.GetCollection());

        AppendLine($"foreach ({type} {name} in {collection}) {{");
        indentLevel++;

        foreach(var stmt in foreachStmt.GetBody().Statements)
        {
            EmitStatement(stmt);
        }

        indentLevel--;
        AppendLine("}");
    }

    // ─── Expression Emission ─────────────────────────────────────────────

    private string EmitExpression(ExpressionNode expr)
    {
        return expr switch
        {
            AssignmentFormExpressionNode assignmentForm => EmitAssignment(assignmentForm.Assignment),
            LambdaFormExpressionNode lambdaForm => EmitLambda(lambdaForm.Lambda),
            _ => throw new InvalidOperationException(
                $"Unknown ExpressionNode variant: {expr.GetType().Name}"),
        };
    }

    private string EmitLambda(LambdaExpressionNode lambda)
    {
        return lambda switch
        {
            ExpressionLambdaLambdaExpressionNode exprLambda =>
                $"{EmitLambdaModifiers(exprLambda.Modifiers)}{EmitLambdaParameterList(exprLambda.Params)} => {EmitExpression(exprLambda.Body)}",
            BlockLambdaLambdaExpressionNode blockLambda =>
                $"{EmitLambdaModifiers(blockLambda.Modifiers)}{EmitLambdaParameterList(blockLambda.Params)} => {EmitBlockBody(blockLambda.Body)}",
            _ => throw new InvalidOperationException(
                $"Unknown LambdaExpressionNode variant: {lambda.GetType().Name}"),
        };
    }

    private static string EmitLambdaModifiers(ImmutableArray<LambdaModifierNode> modifiers)
    {
        if(modifiers.IsDefaultOrEmpty)
        {
            return "";
        }

        var parts = new List<string>();
        foreach(var modifier in modifiers)
        {
            parts.Add(modifier switch
            {
                StaticLambdaModifierLambdaModifierNode => "static",
                AsyncLambdaModifierLambdaModifierNode => "async",
                _ => "",
            });
        }

        return string.Join(" ", parts) + " ";
    }

    private string EmitLambdaParameterList(LambdaParameterListNode paramList)
    {
        return paramList switch
        {
            SingleImplicitParamLambdaParameterListNode singleParam => singleParam.Name.Lexeme,
            ParenthesizedLambdaParamsLambdaParameterListNode parenParams => EmitParenthesizedLambdaParams(parenParams),
            _ => throw new InvalidOperationException(
                $"Unknown LambdaParameterListNode variant: {paramList.GetType().Name}"),
        };
    }

    private string EmitParenthesizedLambdaParams(ParenthesizedLambdaParamsLambdaParameterListNode parenParams)
    {
        if(parenParams.Params is null)
        {
            return "()";
        }

        var parts = new List<string>
        {
            EmitLambdaParam(parenParams.Params.LambdaParam)
        };
        foreach(var param in parenParams.Params.LambdaParams)
        {
            parts.Add(EmitLambdaParam(param));
        }

        return $"({string.Join(", ", parts)})";
    }

    private string EmitLambdaParam(LambdaParamNode param)
    {
        return param switch
        {
            TypedLambdaParamLambdaParamNode typed => $"{EmitTypeReference(typed.Type)} {typed.Name.Lexeme}",
            UntypedLambdaParamLambdaParamNode untyped => untyped.Name.Lexeme,
            _ => throw new InvalidOperationException(
                $"Unknown LambdaParamNode variant: {param.GetType().Name}"),
        };
    }

    private string EmitAssignment(AssignmentNode assignment)
    {
        var target = EmitVoidCoalescing(assignment.Target);

        if(assignment.Value is not null && !ReferenceEquals(GetAssignmentValueTarget(assignment.Value), assignment.Target))
        {
            var value = EmitExpression(assignment.Value);
            return $"{target} = {value}";
        }

        return target;
    }

    /// <summary>
    /// Returns the <see cref="VoidCoalescingNode"/> target of an assignment-RHS expression,
    /// or <c>null</c> when the RHS is a lambda form (which has no assignment target).
    /// </summary>
    private static VoidCoalescingNode? GetAssignmentValueTarget(ExpressionNode value)
    {
        return value.AsAssignmentOrNull()?.Target;
    }

    private string EmitVoidCoalescing(VoidCoalescingNode voidCoalescing)
    {
        var left = EmitLogicalOr(voidCoalescing.Left);

        if(voidCoalescing.LogicalOrs.Length > 0)
        {
            var parts = new List<string> { left };
            foreach(var right in voidCoalescing.LogicalOrs)
            {
                parts.Add(EmitLogicalOr(right));
            }

            return string.Join(" ?? ", parts);
        }

        return left;
    }

    private string EmitLogicalOr(LogicalOrNode logicalOr)
    {
        var left = EmitLogicalAnd(logicalOr.Left);

        if(logicalOr.Right is not null && !ReferenceEquals(logicalOr.Right, logicalOr.Left))
        {
            var right = EmitLogicalAnd(logicalOr.Right);
            return $"{left} || {right}";
        }

        return left;
    }

    private string EmitLogicalAnd(LogicalAndNode logicalAnd)
    {
        var left = EmitEquality(logicalAnd.Left);

        if(logicalAnd.Right is not null && !ReferenceEquals(logicalAnd.Right, logicalAnd.Left))
        {
            var right = EmitEquality(logicalAnd.Right);
            return $"{left} && {right}";
        }

        return left;
    }

    private string EmitEquality(EqualityNode equality)
    {
        var left = EmitRelational(equality.Left);

        if(equality.Right is not null && !ReferenceEquals(equality.Right, equality.Left))
        {
            var op = equality.Operator.Lexeme;
            var right = EmitRelational(equality.Right);
            return $"{left} {op} {right}";
        }

        return left;
    }

    private string EmitRelational(RelationalNode relational)
    {
        switch(relational)
        {
            case RelationalComparisonRelationalNode cmp:
                {
                    var left = EmitAdditive(cmp.Left);

                    if(cmp.Right is not null && !ReferenceEquals(cmp.Right, cmp.Left))
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

            case AsExpressionRelationalNode asExpr:
                return $"{EmitAdditive(asExpr.Left)} as {EmitTypeReference(asExpr.TargetType)}";

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

        if(orPattern.AndPatterns.IsDefaultOrEmpty)
        {
            return left;
        }

        var parts = new List<string> { left };
        foreach(var part in orPattern.AndPatterns)
        {
            parts.Add(EmitAndPattern(part));
        }

        return string.Join(" or ", parts);
    }

    private string EmitRefinementPredicate(RefinementPredicateNode node)
    {
        switch(node)
        {
            case SimpleRefinementPredicateRefinementPredicateNode simple:
                return EmitAndPattern(simple.Predicate);
            case CompoundRefinementPredicateRefinementPredicateNode compound:
                var parts = new List<string> { EmitUnaryPattern(compound.First) };
                foreach(var rest in compound.UnaryPatterns)
                {
                    parts.Add(EmitUnaryPattern(rest));
                }

                return "and " + string.Join(" and ", parts);
            default:
                return "<unknown-refinement>";
        }
    }

    private string EmitAndPattern(AndPatternNode andPattern)
    {
        var left = EmitUnaryPattern(andPattern.Left);

        if(andPattern.UnaryPatterns.IsDefaultOrEmpty)
        {
            return left;
        }

        var parts = new List<string> { left };
        foreach(var part in andPattern.UnaryPatterns)
        {
            parts.Add(EmitUnaryPattern(part));
        }

        return string.Join(" and ", parts);
    }

    private string EmitUnaryPattern(UnaryPatternNode unaryPattern)
    {
        return unaryPattern switch
        {
            NotPatternUnaryPatternNode notPattern => $"not {EmitPrimaryPattern(notPattern.Operand)}",
            DirectPatternUnaryPatternNode directPattern => EmitPrimaryPattern(directPattern.Primary),
            _ => "",
        };
    }

    private string EmitPrimaryPattern(PrimaryPatternNode primaryPattern)
    {
        switch(primaryPattern)
        {
            case TypePatternPrimaryPatternNode typePattern:
                {
                    var type = EmitTypeReference(typePattern.Type);
                    var name = typePattern.Name.Lexeme;
                    return $"{type} {name}";
                }
            case BareTypePatternPrimaryPatternNode bareTypePattern:
                {
                    var type = EmitTypeReference(bareTypePattern.Type);
                    return type;
                }
            case VarPatternPrimaryPatternNode varPattern:
                return $"var {varPattern.Name.Lexeme}";
            case DiscardPatternPrimaryPatternNode:
                return "_";
            case ConstantPatternPrimaryPatternNode constantPattern:
                return EmitConstantLiteral(constantPattern.Literal);
            case LessThanRelationalPatternPrimaryPatternNode ltPattern:
                return $"< {EmitVoidCoalescing(ltPattern.Constant)}";
            case GreaterThanRelationalPatternPrimaryPatternNode gtPattern:
                return $"> {EmitVoidCoalescing(gtPattern.Constant)}";
            case LessThanOrEqualRelationalPatternPrimaryPatternNode ltePattern:
                return $"<= {EmitVoidCoalescing(ltePattern.Constant)}";
            case GreaterThanOrEqualRelationalPatternPrimaryPatternNode gtePattern:
                return $">= {EmitVoidCoalescing(gtePattern.Constant)}";
            case PropertyPatternPrimaryPatternNode propertyPattern:
                {
                    var sb = new StringBuilder();

                    if(propertyPattern.TypeName is not null)
                    {
                        _ = sb.Append(ExtractQualifiedName(propertyPattern.TypeName));
                        _ = sb.Append(' ');
                    }

                    _ = sb.Append("{ ");
                    if(propertyPattern.Subpatterns is not null)
                    {
                        _ = sb.Append(EmitPropertySubpatternList(propertyPattern.Subpatterns));
                        _ = sb.Append(' ');
                    }

                    _ = sb.Append('}');

                    if(propertyPattern.Name.HasValue)
                    {
                        _ = sb.Append(' ');
                        _ = sb.Append(propertyPattern.Name.Value.Lexeme);
                    }

                    return sb.ToString();
                }
            case ListPatternPrimaryPatternNode listPattern:
                {
                    if(listPattern.Elements is null)
                    {
                        return "[]";
                    }

                    return $"[{EmitListPatternElementList(listPattern.Elements)}]";
                }
            case TuplePositionalPatternPrimaryPatternNode tuplePattern:
                {
                    var elements = new List<string>
                    {
                        EmitPattern(tuplePattern.First),
                        EmitPattern(tuplePattern.Rest)
                    };
                    foreach(var additional in tuplePattern.Patterns)
                    {
                        elements.Add(EmitPattern(additional));
                    }

                    return $"({string.Join(", ", elements)})";
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
            RegexConstantConstantLiteralNode regexConst => regexConst.Value.Lexeme,
            TrueConstantConstantLiteralNode => "true",
            FalseConstantConstantLiteralNode => "false",
            _ => "",
        };
    }

    private string EmitPropertySubpatternList(PropertySubpatternListNode subpatternList)
    {
        var parts = new List<string>
        {
            EmitPropertySubpattern(subpatternList.PropertySubpattern)
        };

        foreach(var subpattern in subpatternList.PropertySubpatterns)
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
        var parts = new List<string>
        {
            EmitListPatternElement(elementList.ListPatternElement)
        };

        foreach(var element in elementList.ListPatternElements)
        {
            parts.Add(EmitListPatternElement(element));
        }

        return string.Join(", ", parts);
    }

    private string EmitListPatternElement(ListPatternElementNode element)
    {
        switch(element)
        {
            case PatternElementListPatternElementNode patternElement:
                return EmitPattern(patternElement.Pattern);
            case SliceElementListPatternElementNode sliceElement:
                {
                    if(sliceElement.Name.HasValue)
                    {
                        return $".. {sliceElement.Name.Value.Lexeme}";
                    }

                    return "..";
                }
            default:
                return "";
        }
    }

    private string EmitAdditive(AdditiveNode additive)
    {
        var left = EmitMultiplicative(additive.Left);

        if(additive.Right is not null && !ReferenceEquals(additive.Right, additive.Left))
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

        if(multiplicative.Right is not null && !ReferenceEquals(multiplicative.Right, multiplicative.Left))
        {
            var op = multiplicative.Operator.Lexeme;
            var right = EmitUnary(multiplicative.Right);
            return $"{left} {op} {right}";
        }

        return left;
    }

    private string EmitUnary(UnaryNode unary)
    {
        return unary switch
        {
            AsyncExpressionUnaryNode asyncExpr => $"async {EmitUnary(asyncExpr.Operand)}",
            AwaitExpressionUnaryNode awaitExpr => $"await {EmitUnary(awaitExpr.Operand)}",
            CastExpressionUnaryNode castExpr => $"({EmitTypeReference(castExpr.TargetType)}){EmitUnary(castExpr.Operand)}",
            LogicalNotUnaryNode logicalNot => $"!{EmitUnary(logicalNot.Operand)}",
            NegateUnaryNode negate => $"-{EmitUnary(negate.Operand)}",
            PostfixExpressionUnaryNode postfix => EmitPostfix(postfix.Primary),
            _ => "",
        };
    }

    private string EmitPostfix(PostfixNode postfix)
    {
        var result = EmitPrimary(postfix.Target);

        foreach(var suffix in postfix.Suffix)
        {
            result = EmitPostfixSuffix(result, suffix);
        }

        return result;
    }

    private string EmitPostfixSuffix(string target, PostfixSuffixNode suffix)
    {
        switch(suffix)
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
            case SwitchExpressionSuffixPostfixSuffixNode switchExpr:
                {
                    var sb = new StringBuilder();
                    _ = sb.Append($"{target} switch {{");

                    if(switchExpr.Arms is not null)
                    {
                        void appendArm(StringBuilder builder, SwitchArmNode arm)
                        {
                            _ = builder.Append(' ');
                            _ = builder.Append(EmitPattern(arm.Pattern));
                            if(arm.Guard is not null)
                            {
                                _ = builder.Append($" when {EmitVoidCoalescing(arm.Guard.Condition)}");
                            }

                            _ = builder.Append($" => {EmitExpression(arm.Result)}");
                            _ = builder.Append(',');
                        }

                        appendArm(sb, switchExpr.Arms.SwitchArm);
                        foreach(var arm in switchExpr.Arms.SwitchArms)
                        {
                            appendArm(sb, arm);
                        }
                    }

                    _ = sb.Append(" }");
                    return sb.ToString();
                }
            default:
                return target;
        }
    }

    private string EmitPrimary(PrimaryNode primary)
    {
        switch(primary)
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
            case RegexLiteralExpressionPrimaryNode regexLit:
                return regexLit.RegexLiteral.Lexeme;
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
                    var type = EmitElementTypeRef(arrayCreation.Type);
                    var size = EmitExpression(arrayCreation.Size);
                    return $"new {type}[{size}]";
                }
            case StackallocExpressionPrimaryNode stackallocExpr:
                {
                    // Mirror the array-creation case: stackalloc T[n] reprints with the
                    // element type and bracketed size so parse→print→parse round-trips.
                    var type = EmitElementTypeRef(stackallocExpr.Type);
                    var size = EmitExpression(stackallocExpr.Size);
                    return $"stackalloc {type}[{size}]";
                }
            case ObjectCreationExpressionPrimaryNode objCreation:
                {
                    var type = ExtractQualifiedName(objCreation.Type);
                    var typeArgs = EmitTypeArgumentList(objCreation.TypeArgs);
                    var args = EmitArgumentList(objCreation.Arguments);
                    if(objCreation.Initializer is not null)
                    {
                        var assignments = EmitPropertyAssignmentList(objCreation.Initializer);
                        return $"new {type}{typeArgs}({args}) {{ {assignments} }}";
                    }

                    return $"new {type}{typeArgs}({args})";
                }
            case ObjectCreationExpressionNoArgsPrimaryNode objCreationNoArgs:
                {
                    var type = ExtractQualifiedName(objCreationNoArgs.Type);
                    var typeArgs = EmitTypeArgumentList(objCreationNoArgs.TypeArgs);
                    var assignments = EmitPropertyAssignmentList(objCreationNoArgs.Initializer);
                    return $"new {type}{typeArgs} {{ {assignments} }}";
                }
            case TargetTypedNewExpressionPrimaryNode targetNew:
                {
                    var args = EmitArgumentList(targetNew.Arguments);
                    if(targetNew.Initializer is not null)
                    {
                        var assignments = EmitPropertyAssignmentList(targetNew.Initializer);
                        return $"new({args}) {{ {assignments} }}";
                    }

                    return $"new({args})";
                }
            case TargetTypedNewExpressionNoArgsPrimaryNode targetNewNoArgs:
                {
                    var assignments = EmitPropertyAssignmentList(targetNewNoArgs.Initializer);
                    return $"new {{ {assignments} }}";
                }
            case AnonymousTypeExpressionPrimaryNode anonType:
                {
                    var parts = new List<string>();

                    string formatAnonymousProperty(AnonymousPropertyNode prop)
                    {
                        return prop switch
                        {
                            ExplicitAnonymousPropertyAnonymousPropertyNode explicit_ =>
                                $"{explicit_.Name.Lexeme} = {EmitExpression(explicit_.Value)}",
                            ProjectedAnonymousPropertyAnonymousPropertyNode projected =>
                                EmitPostfix(projected.Projection),
                            _ => ""
                        };
                    }

                    parts.Add(formatAnonymousProperty(anonType.Properties.AnonymousProperty));
                    foreach(var additional in anonType.Properties.AnonymousPropertys)
                    {
                        parts.Add(formatAnonymousProperty(additional));
                    }

                    return $"new {{ {string.Join(", ", parts)} }}";
                }
            case ParenthesizedExpressionPrimaryNode paren:
                return $"({EmitExpression(paren.Inner)})";
            case TupleLiteralExpressionPrimaryNode tupleLit:
                {
                    var parts = new List<string>();

                    string formatTupleLiteralElement(TupleLiteralElementNode element)
                    {
                        return element switch
                        {
                            NamedTupleLiteralElementTupleLiteralElementNode named =>
                                $"{named.Name.Lexeme}: {EmitExpression(named.Value)}",
                            UnnamedTupleLiteralElementTupleLiteralElementNode unnamed =>
                                EmitExpression(unnamed.Value),
                            _ => ""
                        };
                    }

                    parts.Add(formatTupleLiteralElement(tupleLit.First));
                    parts.Add(formatTupleLiteralElement(tupleLit.Rest));
                    foreach(var additional in tupleLit.TupleLiteralElements)
                    {
                        parts.Add(formatTupleLiteralElement(additional));
                    }

                    return $"({string.Join(", ", parts)})";
                }
            case IdentifierExpressionPrimaryNode ident:
                return ident.Name.Lexeme;
            case FieldKeywordExpressionPrimaryNode:
                return "field";
            case TypeLiteralExpressionPrimaryNode typeLiteral:
                return EmitTypeReference(typeLiteral.TypeRef);
            case DefaultExpressionPrimaryNode defaultExpr:
                return $"default({EmitTypeReference(defaultExpr.Type)})";
            case DefaultLiteralExpressionPrimaryNode:
                return "default";
            case ArrayLiteralExpressionPrimaryNode arrayLiteral:
                {
                    var elements = new List<string> { EmitExpression(arrayLiteral.First) };
                    foreach(var elem in arrayLiteral.Expressions)
                    {
                        elements.Add(EmitExpression(elem));
                    }

                    return $"[{string.Join(", ", elements)}]";
                }
            case EmptyArrayLiteralExpressionPrimaryNode:
                return "[]";
            default:
                return "";
        }
    }

    private string EmitInterpolatedString(InterpolatedStringNode interpolated)
    {
        var sb = new StringBuilder();
        _ = sb.Append("$\"");

        foreach(var part in interpolated.Parts)
        {
            switch(part)
            {
                case TextPartInterpolatedPartNode textPart:
                    _ = sb.Append(textPart.Text.Lexeme);
                    break;
                case ExpressionPartInterpolatedPartNode exprPart:
                    _ = sb.Append('{');
                    _ = sb.Append(EmitExpression(exprPart.Expression));
                    _ = sb.Append('}');
                    break;
            }
        }

        _ = sb.Append('"');
        return sb.ToString();
    }

    // ─── Modifier Emission ───────────────────────────────────────────────

    private static string EmitModifiers(ImmutableArray<ModifierNode> modifiers)
    {
        if(modifiers.IsEmpty)
        {
            return "";
        }

        var parts = new List<string>();
        foreach(var mod in modifiers)
        {
            switch(mod)
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
                case ReadonlyModifierModifierNode:
                    parts.Add("readonly");
                    break;
                case OwnedModifierModifierNode:
                    parts.Add("owned");
                    break;
                case AffineModifierModifierNode:
                    parts.Add("affine");
                    break;
                case LinearModifierModifierNode:
                    parts.Add("linear");
                    break;
                case RelevantModifierModifierNode:
                    parts.Add("relevant");
                    break;
                case AsyncModifierModifierNode:
                    parts.Add("async");
                    break;
                case RefModifierModifierNode:
                    parts.Add("ref");
                    break;
            }
        }

        return string.Join(" ", parts) + " ";
    }

    // ─── Utilities ───────────────────────────────────────────────────────

    /// <summary>
    /// Emits a base list (base types / abstract list) as a comma-separated string.
    /// Each entry is a type name with optional type arguments.
    /// </summary>
    private string EmitBaseList(BaseListNode baseList)
    {
        var parts = new List<string>
        {
            EmitBaseTypeRef(baseList.First)
        };
        foreach(var rest in baseList.BaseTypeRefs)
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
    /// Emits a type parameter list (e.g., &lt;T, U&gt; or &lt;T, Size:int&gt;) or empty string if null.
    /// Dependent type parameters are emitted as Name:Type with no spaces around the colon.
    /// </summary>
    private static string EmitTypeParameterList(TypeParameterListNode? typeParams)
    {
        if(typeParams is null)
        {
            return "";
        }

        var parts = new List<string>
        {
            EmitTypeParameter(typeParams.TypeParameter)
        };
        foreach(var tp in typeParams.TypeParameters)
        {
            parts.Add(EmitTypeParameter(tp));
        }

        return $"<{string.Join(", ", parts)}>";
    }

    private static string EmitTypeParameter(TypeParameterNode tp)
    {
        return tp switch
        {
            DependentTypeParameterTypeParameterNode dep =>
                $"{dep.Name.Lexeme}:{EmitPrimitiveType(dep.ValueType)}",
            NamedConstraintDependentTypeParameterTypeParameterNode named =>
                $"{named.Name.Lexeme}:{named.ConstraintName.Lexeme}",
            RegularTypeParameterTypeParameterNode reg =>
                reg.Name.Lexeme,
            _ => tp.GetName().Lexeme,
        };
    }

    /// <summary>
    /// Emits a type argument list (e.g., &lt;int, string&gt;, &lt;5&gt;, &lt;true&gt;) or empty string if null.
    /// Handles all TypeArgument node variants including literal values for dependent type arguments.
    /// </summary>
    private string EmitTypeArgumentList(TypeArgumentListNode? typeArgs)
    {
        if(typeArgs is null)
        {
            return "";
        }

        var parts = new List<string>
        {
            EmitTypeArgument(typeArgs.TypeArgument)
        };
        foreach(var arg in typeArgs.TypeArguments)
        {
            parts.Add(EmitTypeArgument(arg));
        }

        return $"<{string.Join(", ", parts)}>";
    }

    /// <summary>
    /// Emits a single type argument node, handling all variants (literal values and type references).
    /// </summary>
    private string EmitTypeArgument(TypeArgumentNode arg)
    {
        return arg switch
        {
            NegatedIntegerTypeArgumentTypeArgumentNode neg => $"-{neg.Literal.Lexeme}",
            IntegerLiteralTypeArgumentTypeArgumentNode intLit => intLit.Literal.Lexeme,
            HexLiteralTypeArgumentTypeArgumentNode hexLit => hexLit.Literal.Lexeme,
            BinaryLiteralTypeArgumentTypeArgumentNode binLit => binLit.Literal.Lexeme,
            TrueLiteralTypeArgumentTypeArgumentNode => "true",
            FalseLiteralTypeArgumentTypeArgumentNode => "false",
            CharLiteralTypeArgumentTypeArgumentNode charLit => charLit.Literal.Lexeme,
            TypeReferenceTypeArgumentTypeArgumentNode typeRef => EmitTypeReference(typeRef.Type),
            _ => "",
        };
    }

    /// <summary>
    /// Extracts the qualified name from a QualifiedIdentifierNode.
    /// When source text is available, uses the Span to extract the exact text.
    /// Otherwise, reconstructs from the IDENTIFIER and IDENTIFIERs properties.
    /// </summary>
    private string ExtractQualifiedName(QualifiedIdentifierNode qualifiedId)
    {
        // Prefer extracting from source text via Span (handles dotted names correctly)
        if(sourceText is not null)
        {
            var span = qualifiedId.Span;
            if(span.Length > 0 && span.Start + span.Length <= sourceText.Length)
            {
                return sourceText.Substring(span.Start, span.Length);
            }
        }

        // Fall back to reconstructing from token properties (for hand-crafted ASTs)
        var first = qualifiedId.IDENTIFIER.Lexeme;
        if(string.IsNullOrEmpty(first))
        {
            return "";
        }

        if(qualifiedId.IDENTIFIERs.IsDefaultOrEmpty)
        {
            return first;
        }

        // Reconstruct the dotted name: first.second.third...
        var sb = new StringBuilder(first);
        foreach(var token in qualifiedId.IDENTIFIERs)
        {
            if(!string.IsNullOrEmpty(token.Lexeme))
            {
                _ = sb.Append('.');
                _ = sb.Append(token.Lexeme);
            }
        }

        return sb.ToString();
    }

    // ─── Constraint Clauses ─────────────────────────────────────────────

    /// <summary>
    /// Emits where clauses preserving source order.
    /// Returns a string like " where T : class where U : struct" or empty string if no constraints.
    /// Handles both generic constraint clauses and adhoc refinement constraint clauses.
    /// </summary>
    private string EmitConstraintClauses(ImmutableArray<WhereClauseNode> constraints)
    {
        if(constraints.IsDefaultOrEmpty)
        {
            return "";
        }

        var sb = new StringBuilder();
        foreach(var clause in constraints)
        {
            switch(clause)
            {
                case GenericConstraintClauseWhereClauseNode generic:
                    _ = sb.Append(" where ");
                    _ = sb.Append(generic.Name.Lexeme);
                    _ = sb.Append(" : ");
                    _ = sb.Append(EmitConstraintList(generic.Constraints));
                    break;
                case AdhocRefinementConstraintWhereClauseNode refinement:
                    _ = sb.Append(" where ");
                    _ = sb.Append(refinement.Name.Lexeme);
                    _ = sb.Append(' ');
                    _ = sb.Append(EmitAndPattern(refinement.Predicate));
                    break;
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Emits a constraint list as a comma-separated string of constraints.
    /// </summary>
    private string EmitConstraintList(ConstraintListNode constraintList)
    {
        var parts = new List<string>
        {
            EmitConstraint(constraintList.Constraint)
        };
        foreach(var constraint in constraintList.Constraints)
        {
            parts.Add(EmitConstraint(constraint));
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// Emits a single constraint variant.
    /// </summary>
    private string EmitConstraint(ConstraintNode constraint)
    {
        switch(constraint)
        {
            case ClassConstraintConstraintNode:
                return "class";
            case StructConstraintConstraintNode:
                return "struct";
            case ReadonlyConstraintConstraintNode:
                return "readonly";
            case MutableConstraintConstraintNode:
                return "mutable";
            case OwnedConstraintConstraintNode:
                return "owned";
            case DefaultConstraintConstraintNode:
                return "default";
            case ConstructorConstraintConstraintNode ctorConstraint:
                {
                    if(ctorConstraint.ParamTypes is null)
                    {
                        return "new()";
                    }

                    var paramTypes = new List<string>
                    {
                        EmitTypeReference(ctorConstraint.ParamTypes.TypeReference)
                    };
                    foreach(var typeRef in ctorConstraint.ParamTypes.TypeReferences)
                    {
                        paramTypes.Add(EmitTypeReference(typeRef));
                    }

                    return $"new({string.Join(", ", paramTypes)})";
                }
            case TypeConstraintConstraintNode typeConstraint:
                return EmitTypeReference(typeConstraint.Type);
            default:
                return "";
        }
    }

    // ─── Property Initializer Emission ─────────────────────────────────

    private string EmitPropertyAssignmentList(PropertyInitializerBlockNode block)
    {
        if(block.Assignments is null)
        {
            return "";
        }

        var parts = new List<string>
        {
            EmitSinglePropertyAssignment(block.Assignments.PropertyAssignment)
        };
        foreach(var assignment in block.Assignments.PropertyAssignments)
        {
            parts.Add(EmitSinglePropertyAssignment(assignment));
        }

        return string.Join(", ", parts);
    }

    private string EmitSinglePropertyAssignment(PropertyAssignmentNode assignment)
    {
        return assignment switch
        {
            NamedPropertyAssignmentPropertyAssignmentNode named => $"{named.Name.Lexeme} = {EmitExpression(named.Value)}",
            CollectionElementAssignmentPropertyAssignmentNode element => EmitExpression(element.Value),
            _ => ""
        };
    }

    private void AppendLine(string text = "")
    {
        if(string.IsNullOrEmpty(text))
        {
            _ = sb.AppendLine();
        }
        else
        {
            for(var i = 0; i < indentLevel; i++)
            {
                _ = sb.Append(IndentUnit);
            }

            _ = sb.AppendLine(text);
        }
    }
}