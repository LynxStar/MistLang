using System.Collections.Immutable;
using System.Text;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Walks the ASTs (via the semantic model) and emits C# source files into the output directory.
/// Only symbols with SymbolOrigin.UserCode are emitted; Backend symbols are skipped
/// but their namespaces generate using directives.
/// </summary>
public sealed class CSharpEmitter
{
    public static void Emit(ISemanticModel model, string outputDirectory, string projectName, DiagnosticBag diagnostics)
    {
        Directory.CreateDirectory(outputDirectory);
        EmitCsproj(outputDirectory, projectName);

        // Collect namespaces from Backend-origin symbols for using directives.
        var backendNamespaces = CollectBackendNamespaces(model);

        // Emit one .cs file per Mist source file.
        for (var i = 0; i < model.Units.Count; i++)
        {
            var unit = model.Units[i];
            var csFileName = Path.ChangeExtension(
                Path.GetFileName(unit.Source.Path), ".cs");
            var csPath = Path.Combine(outputDirectory, csFileName);

            // Get the AST for this compilation unit (same index as the unit).
            if (i < model.Asts.Length && model.Asts[i] is CompilationUnitNode compilationUnit)
            {
                var writer = new CSharpSourceWriter(model, backendNamespaces, unit.Source);
                var csContent = writer.EmitCompilationUnit(compilationUnit);
                File.WriteAllText(csPath, csContent);
            }
        }
    }

    private static void EmitCsproj(string outputDirectory, string projectName)
    {
        var csprojPath = Path.Combine(outputDirectory, $"{projectName}.csproj");
        var csprojContent = """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <Deterministic>true</Deterministic>
                <EnableNETAnalyzers>false</EnableNETAnalyzers>
              </PropertyGroup>
            </Project>
            """;
        File.WriteAllText(csprojPath, csprojContent);
    }

    private static ImmutableArray<string> CollectBackendNamespaces(ISemanticModel model)
    {
        var namespaces = new HashSet<string>(StringComparer.Ordinal);

        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
            {
                if (symbol.DeclaredType is ClassType ct && ct.ContainingNamespace is not null)
                {
                    namespaces.Add(ct.ContainingNamespace);
                }
                else if (symbol.DeclaredType is StructType st && st.ContainingNamespace is not null)
                {
                    namespaces.Add(st.ContainingNamespace);
                }
                else if (symbol.Kind == SymbolKind.Namespace)
                {
                    namespaces.Add(symbol.Name);
                }
            }
        }

        return [.. namespaces.OrderBy(n => n, StringComparer.Ordinal)];
    }
}

/// <summary>
/// Writes C# source text for a single compilation unit by walking the AST.
/// Uses the semantic model for type information when needed.
/// </summary>
internal sealed class CSharpSourceWriter
{
    private readonly StringBuilder _sb = new();
    private readonly ISemanticModel _model;
    private readonly ImmutableArray<string> _backendNamespaces;
    private readonly Common.Text.SourceText _sourceText;
    private int _indentLevel;
    private const string IndentUnit = "    ";

    public CSharpSourceWriter(
        ISemanticModel model,
        ImmutableArray<string> backendNamespaces,
        Common.Text.SourceText sourceText)
    {
        _model = model;
        _backendNamespaces = backendNamespaces;
        _sourceText = sourceText;
    }

    public string EmitCompilationUnit(CompilationUnitNode cu)
    {
        // Collect using directives from the source.
        var usings = new HashSet<string>(StringComparer.Ordinal);

        foreach (var usingDecl in cu.Usings)
        {
            var name = ExtractQualifiedName(usingDecl.Name);
            usings.Add(name);
        }

        // Also add backend namespaces that are referenced.
        foreach (var ns in _backendNamespaces)
        {
            usings.Add(ns);
        }

        // Emit using directives.
        foreach (var ns in usings.OrderBy(n => n, StringComparer.Ordinal))
        {
            AppendLine($"using {ns};");
        }

        if (usings.Count > 0)
        {
            AppendLine();
        }

        // Emit namespace declaration.
        if (cu.Namespace is not null)
        {
            EmitNamespaceDeclaration(cu.Namespace);
        }

        // Emit top-level type declarations (outside namespace).
        foreach (var typeDecl in cu.Types)
        {
            EmitTypeDeclaration(typeDecl);
        }

        return _sb.ToString();
    }

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
                AppendLine($"namespace {name}");
                AppendLine("{");
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
        // Check if this is a Backend symbol — skip if so.
        var symbol = _model.GetDeclaredSymbol(classDecl);
        if (symbol is not null && symbol.Origin == SymbolOrigin.Backend)
        {
            return;
        }

        var modifiers = EmitModifiers(classDecl.Modifiers);
        var name = classDecl.Name.Lexeme;
        AppendLine($"{modifiers}class {name}");
        AppendLine("{");
        _indentLevel++;

        for (var i = 0; i < classDecl.Members.Length; i++)
        {
            if (i > 0)
            {
                AppendLine();
            }
            EmitMemberDeclaration(classDecl.Members[i]);
        }

        _indentLevel--;
        AppendLine("}");
    }

    private void EmitStructDeclaration(StructDeclarationNode structDecl)
    {
        var symbol = _model.GetDeclaredSymbol(structDecl);
        if (symbol is not null && symbol.Origin == SymbolOrigin.Backend)
        {
            return;
        }

        var modifiers = EmitModifiers(structDecl.Modifiers);
        var name = structDecl.Name.Lexeme;
        AppendLine($"{modifiers}struct {name}");
        AppendLine("{");
        _indentLevel++;

        for (var i = 0; i < structDecl.Members.Length; i++)
        {
            if (i > 0)
            {
                AppendLine();
            }
            EmitMemberDeclaration(structDecl.Members[i]);
        }

        _indentLevel--;
        AppendLine("}");
    }

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

        if (fieldDecl.Initializer.HasValue)
        {
            // The field has an initializer expression.
            // The grammar defines initializer as (EQUAL Expression)? which means
            // the Storm generator stores the EQUAL token. We need to find the
            // expression from the AST children.
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
        else
        {
            AppendLine($"{modifiers}{type} {name};");
        }
    }

    private static ExpressionNode? FindFieldInitializerExpression(FieldDeclarationNode fieldDecl)
    {
        // Walk children to find the ExpressionNode.
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
        if (ctorDecl.Chain is not null)
        {
            chain = " : this(" + EmitArgumentList(ctorDecl.Chain.Arguments) + ")";
        }

        AppendLine($"{modifiers}{name}({parameters}){chain}");
        EmitBlock(ctorDecl.Body);
    }

    private void EmitMethodDeclaration(MethodDeclarationNode methodDecl)
    {
        var modifiers = EmitModifiers(methodDecl.Modifiers);
        var returnType = EmitTypeReference(methodDecl.ReturnType);
        var name = methodDecl.Name.Lexeme;
        var parameters = EmitParameterList(methodDecl.Parameters);

        AppendLine($"{modifiers}{returnType} {name}({parameters})");
        EmitBlock(methodDecl.Body);
    }

    private string EmitParameterList(ParameterListNode? paramList)
    {
        if (paramList is null) return "";

        // The Storm generator only stores the first parameter as a named property;
        // additional comma-separated parameters are parsed but discarded from the AST.
        // Use the source span to faithfully reproduce the full parameter list.
        var span = paramList.Span;
        if (span.Length > 0 && span.Start + span.Length <= _sourceText.Contents.Length)
        {
            return _sourceText.Contents.Substring(span.Start, span.Length);
        }

        // Fallback: emit the single parameter we do have.
        return EmitParameter(paramList.Parameter);
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

        // The Storm generator only stores the first expression as a named property;
        // additional comma-separated arguments are parsed but discarded from the AST.
        // Use the source span to faithfully reproduce the full argument list.
        var span = argList.Span;
        if (span.Length > 0 && span.Start + span.Length <= _sourceText.Contents.Length)
        {
            return _sourceText.Contents.Substring(span.Start, span.Length);
        }

        // Fallback: emit the single argument we do have.
        return EmitExpression(argList.Expression);
    }

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

    private void EmitBlock(BlockNode block)
    {
        AppendLine("{");
        _indentLevel++;

        foreach (var stmt in block.Statements)
        {
            EmitStatement(stmt);
        }

        _indentLevel--;
        AppendLine("}");
    }

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
        AppendLine($"if ({condition})");
        EmitBlock(ifStmt.ThenBranch);

        if (ifStmt.ElseBranch is not null)
        {
            EmitElseClause(ifStmt.ElseBranch);
        }
    }

    private void EmitElseClause(ElseClauseNode elseClause)
    {
        switch (elseClause)
        {
            case ElseIfElseClauseNode elseIf:
            {
                var condition = EmitExpression(elseIf.ElseIf.Condition);
                AppendLine($"else if ({condition})");
                EmitBlock(elseIf.ElseIf.ThenBranch);
                if (elseIf.ElseIf.ElseBranch is not null)
                {
                    EmitElseClause(elseIf.ElseIf.ElseBranch);
                }
                break;
            }
            case ElseBlockElseClauseNode elseBlock:
                AppendLine("else");
                EmitBlock(elseBlock.ElseBlock);
                break;
        }
    }

    private void EmitWhileStatement(WhileStatementNode whileStmt)
    {
        var condition = EmitExpression(whileStmt.Condition);
        AppendLine($"while ({condition})");
        EmitBlock(whileStmt.Body);
    }

    private void EmitForStatement(ForStatementNode forStmt)
    {
        var init = EmitForInitializer(forStmt.Initializer);
        var condition = forStmt.Condition is not null ? EmitExpression(forStmt.Condition) : "";
        var iterator = forStmt.Iterator is not null ? EmitExpression(forStmt.Iterator) : "";

        AppendLine($"for ({init}; {condition}; {iterator})");
        EmitBlock(forStmt.Body);
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

        AppendLine($"foreach ({type} {name} in {collection})");
        EmitBlock(foreachStmt.Body);
    }

    // ─── Expression Emission ─────────────────────────────────────────────

    private string EmitExpression(ExpressionNode expr)
    {
        return EmitAssignment(expr.Assignment);
    }

    private string EmitAssignment(AssignmentNode assignment)
    {
        var target = EmitLogicalOr(assignment.Target);

        if (assignment.Value is not null)
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
                return $"typeof({EmitTypeReference(typeLiteral.TypeRef)})";
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

    private string ExtractQualifiedName(QualifiedIdentifierNode qualifiedId)
    {
        var span = qualifiedId.Span;
        if (span.Length > 0 && span.Start + span.Length <= _sourceText.Contents.Length)
        {
            return _sourceText.Contents.Substring(span.Start, span.Length);
        }

        return qualifiedId.IDENTIFIER.Lexeme;
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
