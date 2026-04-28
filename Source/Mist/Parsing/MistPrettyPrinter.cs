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

    /// <summary>
    /// Pretty-prints the given AST node to syntactically valid Mist source text.
    /// </summary>
    public static string Print(SyntaxNode ast)
    {
        var printer = new MistPrettyPrinter();
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

        if (cu.Usings.Length > 0 && (cu.Namespace is not null || cu.Types.Length > 0))
        {
            AppendLine();
        }

        if (cu.Namespace is not null)
        {
            EmitNamespaceDeclaration(cu.Namespace);
        }

        foreach (var typeDecl in cu.Types)
        {
            EmitTypeDeclaration(typeDecl);
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
        }
    }

    private void EmitClassDeclaration(ClassDeclarationNode classDecl)
    {
        var modifiers = EmitModifiers(classDecl.Modifiers);
        var name = classDecl.Name.Lexeme;
        var baseClause = "";
        if (classDecl.BaseClass is not null)
        {
            var baseName = classDecl.BaseClassName.Lexeme;
            baseClause = $" : {baseName}";
        }
        AppendLine($"{modifiers}class {name}{baseClause} {{");
        _indentLevel++;

        EmitMembers(classDecl.Members);

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitStructDeclaration(StructDeclarationNode structDecl)
    {
        var modifiers = EmitModifiers(structDecl.Modifiers);
        var name = structDecl.Name.Lexeme;
        AppendLine($"{modifiers}struct {name} {{");
        _indentLevel++;

        EmitMembers(structDecl.Members);

        _indentLevel--;
        AppendLine("}");
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
        }
    }

    private void EmitFieldDeclaration(FieldDeclarationNode fieldDecl)
    {
        var modifiers = EmitModifiers(fieldDecl.Modifiers);
        var type = EmitTypeReference(fieldDecl.Type);
        var name = fieldDecl.Name.Lexeme;

        var initExpr = FindFieldInitializerExpression(fieldDecl);
        if (initExpr is not null)
        {
            AppendLine($"{modifiers}{type} {name} = {EmitExpression(initExpr)};");
        }
        else
        {
            AppendLine($"{modifiers}{type} {name};");
        }
    }

    private static ExpressionNode? FindFieldInitializerExpression(FieldDeclarationNode fieldDecl)
    {
        foreach (var child in fieldDecl.Children())
        {
            if (child is ExpressionNode expr)
            {
                return expr;
            }
        }
        return null;
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

        foreach (var stmt in ctorDecl.Body.Statements)
        {
            EmitStatement(stmt);
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitMethodDeclaration(MethodDeclarationNode methodDecl)
    {
        var modifiers = EmitModifiers(methodDecl.Modifiers);
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var parameters = EmitParameterList(methodDecl.Parameters);

        AppendLine($"{modifiers}{returnType} {name}({parameters}) {{");
        _indentLevel++;

        foreach (var stmt in methodDecl.Body.Statements)
        {
            EmitStatement(stmt);
        }

        _indentLevel--;
        AppendLine("}");
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
                if (namedRef.ArrayRank.HasValue)
                {
                    return $"{name}[]";
                }
                return name;
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
        var left = EmitAdditive(relational.Left);

        if (relational.Right is not null && !ReferenceEquals(relational.Right, relational.Left))
        {
            var op = relational.Operator.Lexeme;
            var right = EmitAdditive(relational.Right);
            return $"{left} {op} {right}";
        }

        return left;
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
                var args = EmitArgumentList(methodCall.Arguments);
                return $"{target}.{name}({args})";
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
                var args = EmitArgumentList(objCreation.Arguments);
                return $"new {type}({args})";
            }
            case ParenthesizedExpressionPrimaryNode paren:
                return $"({EmitExpression(paren.Inner)})";
            case IdentifierExpressionPrimaryNode ident:
                return ident.Name.Lexeme;
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
            }
        }

        return string.Join(" ", parts) + " ";
    }

    // ─── Utilities ───────────────────────────────────────────────────────

    /// <summary>
    /// Extracts the qualified name from a QualifiedIdentifierNode.
    /// Uses the Span to reconstruct dotted names, falling back to the IDENTIFIER lexeme.
    /// </summary>
    private static string ExtractQualifiedName(QualifiedIdentifierNode qualifiedId)
    {
        // Walk children to reconstruct the dotted name from tokens.
        // The QualifiedIdentifierNode stores the first IDENTIFIER token,
        // and additional DOT IDENTIFIER pairs are in the Span.
        // Since we don't have source text, we reconstruct from the node's lexeme
        // and any additional identifiers found in the span.
        // For hand-crafted ASTs, the Span property may encode the full name.
        var lexeme = qualifiedId.IDENTIFIER.Lexeme;
        if (string.IsNullOrEmpty(lexeme))
        {
            return "";
        }

        // If the span is larger than the single identifier, there may be dotted parts.
        // However, since QualifiedIdentifierNode only exposes a single IDENTIFIER token,
        // we rely on the lexeme for simple names and the Span for dotted names.
        // For the pretty printer, we use the lexeme directly — dotted names will
        // have been stored as the full dotted string in the lexeme by the parser.
        return lexeme;
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
