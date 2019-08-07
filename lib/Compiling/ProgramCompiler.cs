using System.Collections.Generic;

public sealed class ProgramCompiler
{
	public Compiler compiler;
	public readonly ParseRule[] parseRules = new ParseRule[(int)TokenKind.COUNT];

	public ProgramCompiler()
	{
		ParseRules.InitRulesFor(this);
		compiler = new Compiler();
	}

	public Result<ByteCodeChunk, List<CompileError>> Compile(string source)
	{
		compiler.Reset(source);
		compiler.parser.Next();

		while (!compiler.parser.Match(TokenKind.End))
			Declaration();

		compiler.EmitInstruction(Instruction.Halt);

		if (compiler.errors.Count > 0)
			return Result.Error(compiler.errors);
		return Result.Ok(compiler.chunk);
	}

	public void ParseWithPrecedence(Precedence precedence)
	{
		var parser = compiler.parser;
		parser.Next();
		if (parser.previousToken.kind == TokenKind.End)
			return;

		var prefixRule = parseRules[(int)parser.previousToken.kind].prefixRule;
		if (prefixRule == null)
		{
			compiler.AddHardError(parser.previousToken.slice, "Expected expression");
			return;
		}
		prefixRule(precedence);

		while (
			parser.currentToken.kind != TokenKind.End &&
			precedence <= parseRules[(int)parser.currentToken.kind].precedence
		)
		{
			parser.Next();
			var infixRule = parseRules[(int)parser.previousToken.kind].infixRule;
			infixRule(precedence);
		}

		var canAssign = precedence <= Precedence.Assignment;
		if (canAssign && compiler.parser.Match(TokenKind.Equal))
		{
			compiler.AddHardError(compiler.parser.previousToken.slice, "Invalid assignment target");
			Expression();
		}
	}

	public Result<ByteCodeChunk, List<CompileError>> CompileExpression(string source)
	{
		compiler.parser.Next();
		Expression();
		compiler.EmitInstruction(Instruction.Halt);

		if (compiler.errors.Count > 0)
			return Result.Error(compiler.errors);
		return Result.Ok(compiler.chunk);
	}

	private void Syncronize()
	{
		if (!compiler.isInPanicMode)
			return;

		while (compiler.parser.currentToken.kind != TokenKind.End)
		{
			switch ((TokenKind)compiler.parser.currentToken.kind)
			{
			case TokenKind.Function:
				compiler.isInPanicMode = false;
				return;
			default:
				break;
			}

			compiler.parser.Next();
		}
	}

	public void Declaration()
	{
		if (compiler.parser.Match(TokenKind.Function))
			FunctionDeclaration();
		else if (compiler.parser.Match(TokenKind.Struct))
			StructDeclaration();
		else
			compiler.AddHardError(compiler.parser.previousToken.slice, "Expected function or struct declaration");
		Syncronize();
	}

	public void FunctionDeclaration()
	{
		compiler.parser.Consume(TokenKind.Identifier, "Expected function name");
		ConsumeFunction(compiler.parser.previousToken.slice);
	}

	public void FunctionExpression(Precedence precedence)
	{
		ConsumeFunction(new Slice());
		var functionIndex = compiler.chunk.functions.count - 1;
		var function = compiler.chunk.functions.buffer[functionIndex];

		compiler.EmitLoadFunction(functionIndex);
		compiler.typeStack.PushBack(ValueTypeHelper.SetIndex(ValueType.Function, function.typeIndex));
	}

	private void ConsumeFunction(Slice slice)
	{
		const int MaxParamCount = 8;

		var source = compiler.parser.tokenizer.source;
		var declaration = compiler.BeginFunctionDeclaration();
		var paramStartIndex = compiler.localVariables.count;

		compiler.parser.Consume(TokenKind.OpenParenthesis, "Expected '(' after function name");
		if (!compiler.parser.Check(TokenKind.CloseParenthesis))
		{
			do
			{
				compiler.parser.Consume(TokenKind.Identifier, "Expected parameter name");
				var paramSlice = compiler.parser.previousToken.slice;
				compiler.parser.Consume(TokenKind.Colon, "Expected ':' after parameter name");
				var paramType = compiler.ConsumeType("Expected parameter type", 0);

				if (declaration.parameterCount >= MaxParamCount)
				{
					compiler.AddSoftError(paramSlice, "Function can not have more than {0} parameters", MaxParamCount);
					continue;
				}

				var hasDuplicate = false;
				for (var i = 0; i < declaration.parameterCount; i++)
				{
					var otherSlice = compiler.localVariables.buffer[paramStartIndex + i].slice;
					if (CompilerHelper.AreEqual(source, paramSlice, otherSlice))
					{
						hasDuplicate = true;
						break;
					}
				}

				if (hasDuplicate)
				{
					compiler.AddSoftError(paramSlice, "Function already has a parameter with this name");
					continue;
				}

				compiler.AddLocalVariable(paramSlice, paramType, false, true);
				declaration.AddParam(paramType);
			} while (compiler.parser.Match(TokenKind.Comma));
		}
		compiler.parser.Consume(TokenKind.CloseParenthesis, "Expected ')' after function parameter list");

		if (compiler.parser.Match(TokenKind.Colon))
			declaration.returnType = compiler.ConsumeType("Expected function return type", 0);

		compiler.EndFunctionDeclaration(declaration, slice);
		compiler.functionReturnTypeStack.PushBack(declaration.returnType);

		compiler.parser.Consume(TokenKind.OpenCurlyBrackets, "Expected '{' before function body");

		if (declaration.returnType == ValueType.Unit)
		{
			BlockStatement();
			compiler.EmitInstruction(Instruction.LoadUnit);
		}
		else
		{
			Block(Precedence.None);
			var type = compiler.typeStack.PopLast();
			if (declaration.returnType != type)
				compiler.AddSoftError(compiler.parser.previousToken.slice, "Wrong return type. Expected {0}. Got {1}", declaration.returnType.ToString(compiler.chunk), type.ToString(compiler.chunk));
		}

		compiler.EmitInstruction(Instruction.Return);

		compiler.functionReturnTypeStack.PopLast();
		compiler.localVariables.count -= declaration.parameterCount;
	}

	public void StructDeclaration()
	{
		compiler.parser.Consume(TokenKind.Identifier, "Expected struct name");
		var slice = compiler.parser.previousToken.slice;

		var source = compiler.parser.tokenizer.source;
		var declaration = compiler.BeginStructDeclaration();
		var fieldStartIndex = compiler.chunk.structTypeFields.count;

		compiler.parser.Consume(TokenKind.OpenCurlyBrackets, "Expected '{' before struct fields");
		while (
			!compiler.parser.Check(TokenKind.CloseCurlyBrackets) &&
			!compiler.parser.Check(TokenKind.End)
		)
		{
			compiler.parser.Consume(TokenKind.Identifier, "Expected field name");
			var fieldSlice = compiler.parser.previousToken.slice;
			compiler.parser.Consume(TokenKind.Colon, "Expected ':' after field name");
			var fieldType = compiler.ConsumeType("Expected field type", 0);

			var hasDuplicate = false;
			for (var i = 0; i < declaration.fieldCount; i++)
			{
				var otherName = compiler.chunk.structTypeFields.buffer[fieldStartIndex + i].name;
				if (CompilerHelper.AreEqual(source, fieldSlice, otherName))
				{
					hasDuplicate = true;
					break;
				}
			}

			if (hasDuplicate)
			{
				compiler.AddSoftError(fieldSlice, "Struct already has a field with this name");
				continue;
			}

			var fieldName = CompilerHelper.GetSlice(compiler, fieldSlice);
			declaration.AddField(fieldName, fieldType);
		}
		compiler.parser.Consume(TokenKind.CloseCurlyBrackets, "Expected '}' after struct fields");

		compiler.EndStructDeclaration(declaration, slice);
	}

	public Option<ValueType> Statement()
	{
		if (compiler.parser.Match(TokenKind.OpenCurlyBrackets))
		{
			BlockStatement();
			return Option.None;
		}
		else if (compiler.parser.Match(TokenKind.Let))
		{
			VariableDeclaration(false);
			return Option.None;
		}
		else if (compiler.parser.Match(TokenKind.Mut))
		{
			VariableDeclaration(true);
			return Option.None;
		}
		else if (compiler.parser.Match(TokenKind.While))
		{
			WhileStatement();
			return Option.None;
		}
		else if (compiler.parser.Match(TokenKind.For))
		{
			ForStatement();
			return Option.None;
		}
		else if (compiler.parser.Match(TokenKind.Break))
		{
			BreakStatement();
			return Option.None;
		}
		else if (compiler.parser.Match(TokenKind.Return))
		{
			var type = ReturnStatement();
			return Option.Some(type);
		}
		else if (compiler.parser.Match(TokenKind.Print))
		{
			PrintStatement();
			return Option.None;
		}
		else
		{
			var type = ExpressionStatement();
			return Option.Some(type);
		}
	}

	public ValueType ExpressionStatement()
	{
		Expression();
		compiler.EmitInstruction(Instruction.Pop);
		return compiler.typeStack.count > 0 ?
			compiler.typeStack.PopLast() :
			ValueType.Unit;
	}

	public void BlockStatement()
	{
		var scope = compiler.BeginScope();
		while (
			!compiler.parser.Check(TokenKind.CloseCurlyBrackets) &&
			!compiler.parser.Check(TokenKind.End)
		)
		{
			Statement();
		}

		compiler.parser.Consume(TokenKind.CloseCurlyBrackets, "Expected '}' after block.");
		compiler.EndScope(scope);
	}

	private int VariableDeclaration(bool mutable)
	{
		compiler.parser.Consume(TokenKind.Identifier, "Expected variable name");
		var slice = compiler.parser.previousToken.slice;

		compiler.parser.Consume(TokenKind.Equal, "Expected assignment");
		Expression();

		return compiler.DeclareLocalVariable(slice, mutable);
	}

	public void WhileStatement()
	{
		var loopJump = compiler.BeginEmitBackwardJump();
		Expression();

		if (compiler.typeStack.PopLast() != ValueType.Bool)
			compiler.AddSoftError(compiler.parser.previousToken.slice, "Expected bool expression as while condition");

		compiler.parser.Consume(TokenKind.OpenCurlyBrackets, "Expected '{' after while statement");

		var breakJump = compiler.BeginEmitForwardJump(Instruction.PopAndJumpForwardIfFalse);
		compiler.BeginLoop();
		BlockStatement();

		compiler.EndEmitBackwardJump(Instruction.JumpBackward, loopJump);
		compiler.EndEmitForwardJump(breakJump);
		compiler.EndLoop();
	}

	public void ForStatement()
	{
		var scope = compiler.BeginScope();
		var itVarIndex = VariableDeclaration(true);
		compiler.localVariables.buffer[itVarIndex].isUsed = true;
		var itVar = compiler.localVariables.buffer[itVarIndex];
		if (itVar.type != ValueType.Int)
			compiler.AddSoftError(itVar.slice, "Expected variable of type int in for loop");

		compiler.parser.Consume(TokenKind.Comma, "Expected comma after begin expression");
		Expression();
		var toVarIndex = compiler.DeclareLocalVariable(compiler.parser.previousToken.slice, false);
		compiler.localVariables.buffer[toVarIndex].isUsed = true;
		if (compiler.localVariables.buffer[toVarIndex].type != ValueType.Int)
			compiler.AddSoftError(compiler.parser.previousToken.slice, "Expected expression of type int");

		compiler.parser.Consume(TokenKind.OpenCurlyBrackets, "Expected '{' after while statement");

		var loopJump = compiler.BeginEmitBackwardJump();
		compiler.EmitInstruction(Instruction.ForLoopCheck);
		compiler.EmitByte((byte)itVar.stackIndex);

		var breakJump = compiler.BeginEmitForwardJump(Instruction.PopAndJumpForwardIfFalse);
		compiler.BeginLoop();
		BlockStatement();

		compiler.EmitInstruction(Instruction.IncrementLocalInt);
		compiler.EmitByte((byte)itVar.stackIndex);

		compiler.EndEmitBackwardJump(Instruction.JumpBackward, loopJump);
		compiler.EndEmitForwardJump(breakJump);
		compiler.EndLoop();

		compiler.EndScope(scope);
	}

	private void BreakStatement()
	{
		var breakJump = compiler.BeginEmitForwardJump(Instruction.JumpForward);

		var nestingCount = 1;
		if (compiler.parser.Match(TokenKind.IntLiteral))
		{
			nestingCount = CompilerHelper.GetInt(compiler);

			if (nestingCount <= 0)
			{
				compiler.AddSoftError(compiler.parser.previousToken.slice, "Nesting count must be at least 1");
				nestingCount = 1;
			}

			if (nestingCount > compiler.loopNesting)
			{
				compiler.AddSoftError(compiler.parser.previousToken.slice, "Nesting count can not exceed loop nesting count which is {0}", compiler.loopNesting);
				nestingCount = compiler.loopNesting;
			}
		}

		if (!compiler.BreakLoop(nestingCount, breakJump))
		{
			compiler.AddSoftError(compiler.parser.previousToken.slice, "Not inside a loop");
			return;
		}
	}

	private ValueType ReturnStatement()
	{
		var expectedType = compiler.functionReturnTypeStack.buffer[compiler.functionReturnTypeStack.count - 1];
		var returnType = ValueType.Unit;

		if (expectedType != ValueType.Unit)
		{
			Expression();
			if (compiler.typeStack.count > 0)
				returnType = compiler.typeStack.PopLast();
		}
		else
		{
			compiler.EmitInstruction(Instruction.LoadUnit);
		}

		compiler.EmitInstruction(Instruction.Return);
		if (expectedType != returnType)
			compiler.AddSoftError(compiler.parser.previousToken.slice, "Wrong return type. Expected {0}. Got {1}", expectedType.ToString(compiler.chunk), returnType.ToString(compiler.chunk));

		return returnType;
	}

	private void PrintStatement()
	{
		Expression();
		compiler.EmitInstruction(Instruction.Print);
		compiler.typeStack.PopLast();
	}

	public void Expression()
	{
		ParseWithPrecedence(Precedence.Assignment);
	}

	public void Grouping(Precedence precedence)
	{
		Expression();
		compiler.parser.Consume(TokenKind.CloseParenthesis, "Expected ')' after expression");
	}

	public void Block(Precedence precedence)
	{
		var scope = compiler.BeginScope();
		var maybeType = new Option<ValueType>();

		while (
			!compiler.parser.Check(TokenKind.CloseCurlyBrackets) &&
			!compiler.parser.Check(TokenKind.End)
		)
		{
			maybeType = Statement();
		}

		if (maybeType.isSome)
		{
			compiler.chunk.bytes.count -= 1;

			var varCount = compiler.localVariables.count - scope.localVarStartIndex;
			if (varCount > 0)
			{
				compiler.EmitInstruction(Instruction.CopyTo);
				compiler.EmitByte((byte)varCount);
			}
		}

		compiler.parser.Consume(TokenKind.CloseCurlyBrackets, "Expected '}' after block.");

		compiler.EndScope(scope);

		if (maybeType.isSome)
		{
			compiler.typeStack.PushBack(maybeType.value);
		}
		else
		{
			compiler.typeStack.PushBack(ValueType.Unit);
			compiler.EmitInstruction(Instruction.LoadUnit);
		}
	}

	public void If(Precedence precedence)
	{
		Expression();

		if (compiler.typeStack.PopLast() != ValueType.Bool)
			compiler.AddSoftError(compiler.parser.previousToken.slice, "Expected bool expression as if condition");

		compiler.parser.Consume(TokenKind.OpenCurlyBrackets, "Expected '{' after if expression");

		var elseJump = compiler.BeginEmitForwardJump(Instruction.PopAndJumpForwardIfFalse);
		Block(precedence);
		var thenType = compiler.typeStack.PopLast();

		var thenJump = compiler.BeginEmitForwardJump(Instruction.JumpForward);
		compiler.EndEmitForwardJump(elseJump);

		if (compiler.parser.Match(TokenKind.Else))
		{
			if (compiler.parser.Match(TokenKind.If))
			{
				If(precedence);
			}
			else
			{
				compiler.parser.Consume(TokenKind.OpenCurlyBrackets, "Expected '{' after else");
				Block(precedence);
			}

			var elseType = compiler.typeStack.PopLast();
			if (thenType != elseType)
				compiler.AddSoftError(compiler.parser.previousToken.slice, "If expression must produce values of the same type on both branches. Found types: {0} and {1}", thenType, elseType);
		}
		else
		{
			compiler.EmitInstruction(Instruction.LoadUnit);
			if (thenType != ValueType.Unit)
				compiler.AddSoftError(compiler.parser.previousToken.slice, "If expression must not produce a value when there is no else branch. Found type: {0}. Try ending with '{}'", thenType);
		}

		compiler.EndEmitForwardJump(thenJump);
		compiler.typeStack.PushBack(thenType);
	}

	public void And(Precedence precedence)
	{
		if (compiler.typeStack.PopLast() != ValueType.Bool)
			compiler.AddSoftError(compiler.parser.previousToken.slice, "Expected bool expression before and");

		var jump = compiler.BeginEmitForwardJump(Instruction.JumpForwardIfFalse);
		compiler.EmitInstruction(Instruction.Pop);
		ParseWithPrecedence(Precedence.And);
		compiler.EndEmitForwardJump(jump);

		if (compiler.typeStack.PopLast() != ValueType.Bool)
			compiler.AddSoftError(compiler.parser.previousToken.slice, "Expected bool expression after and");

		compiler.typeStack.PushBack(ValueType.Bool);
	}

	public void Or(Precedence precedence)
	{
		if (compiler.typeStack.PopLast() != ValueType.Bool)
			compiler.AddSoftError(compiler.parser.previousToken.slice, "Expected bool expression before or");

		var jump = compiler.BeginEmitForwardJump(Instruction.JumpForwardIfTrue);
		compiler.EmitInstruction(Instruction.Pop);
		ParseWithPrecedence(Precedence.Or);
		compiler.EndEmitForwardJump(jump);

		if (compiler.typeStack.PopLast() != ValueType.Bool)
			compiler.AddSoftError(compiler.parser.previousToken.slice, "Expected bool expression after or");

		compiler.typeStack.PushBack(ValueType.Bool);
	}

	public void Literal(Precedence precedence)
	{
		switch ((TokenKind)compiler.parser.previousToken.kind)
		{
		case TokenKind.True:
			compiler.EmitInstruction(Instruction.LoadTrue);
			compiler.typeStack.PushBack(ValueType.Bool);
			break;
		case TokenKind.False:
			compiler.EmitInstruction(Instruction.LoadFalse);
			compiler.typeStack.PushBack(ValueType.Bool);
			break;
		case TokenKind.IntLiteral:
			compiler.EmitLoadLiteral(
				new ValueData(CompilerHelper.GetInt(compiler)),
				ValueType.Int
			);
			compiler.typeStack.PushBack(ValueType.Int);
			break;
		case TokenKind.FloatLiteral:
			compiler.EmitLoadLiteral(
				new ValueData(CompilerHelper.GetFloat(compiler)),
				ValueType.Float
			);
			compiler.typeStack.PushBack(ValueType.Float);
			break;
		case TokenKind.StringLiteral:
			compiler.EmitLoadStringLiteral(CompilerHelper.GetString(compiler));
			compiler.typeStack.PushBack(ValueType.String);
			break;
		default:
			compiler.AddHardError(
				compiler.parser.previousToken.slice,
				string.Format("Expected literal. Got {0}", compiler.parser.previousToken.kind)
			);
			compiler.typeStack.PushBack(ValueType.Unit);
			break;
		}
	}

	public void Variable(Precedence precedence)
	{
		var slice = compiler.parser.previousToken.slice;
		var index = compiler.ResolveToLocalVariableIndex();

		var canAssign = precedence <= Precedence.Assignment;
		if (canAssign && compiler.parser.Match(TokenKind.Equal))
		{
			Expression();

			if (index < 0)
			{
				compiler.AddSoftError(slice, "Can not write to undeclared variable. Try declaring it with 'let' or 'mut'");
			}
			else
			{
				var localVar = compiler.localVariables.buffer[index];
				if (!localVar.isMutable)
					compiler.AddSoftError(slice, "Can not write to immutable variable. Try using 'mut' instead of 'let'");

				compiler.EmitInstruction(Instruction.AssignLocal);
				compiler.EmitByte((byte)localVar.stackIndex);
			}
		}
		else
		{
			if (index < 0)
			{
				var functionIndex = compiler.ResolveToFunctionIndex();
				if (functionIndex < 0)
				{
					compiler.AddSoftError(slice, "Can not read undeclared variable. Declare it with 'let'");
					compiler.typeStack.PushBack(ValueType.Unit);
				}
				else
				{
					compiler.EmitLoadFunction(functionIndex);
					var function = compiler.chunk.functions.buffer[functionIndex];
					var type = ValueTypeHelper.SetIndex(ValueType.Function, function.typeIndex);
					compiler.typeStack.PushBack(type);
				}
			}
			else
			{
				ref var localVar = ref compiler.localVariables.buffer[index];
				localVar.isUsed = true;

				compiler.EmitInstruction(Instruction.LoadLocal);
				compiler.EmitByte((byte)localVar.stackIndex);
				compiler.typeStack.PushBack(localVar.type);
			}
		}
	}

	public void Call(Precedence precedence)
	{
		var slice = compiler.parser.previousToken.slice;

		var functionType = new FunctionType();
		var type = compiler.typeStack.PopLast();

		var hasFunction = false;
		if (ValueTypeHelper.GetKind(type) == ValueType.Function)
		{
			functionType = compiler.chunk.functionTypes.buffer[ValueTypeHelper.GetIndex(type)];
			hasFunction = true;
		}
		else
		{
			compiler.AddSoftError(slice, "Callee must be a function");
		}

		var argIndex = 0;
		if (!compiler.parser.Check(TokenKind.CloseParenthesis))
		{
			do
			{
				Expression();
				var argType = compiler.typeStack.PopLast();
				if (
					hasFunction &&
					argIndex < functionType.parameters.length
				)
				{
					var paramType = compiler.chunk.functionTypeParams.buffer[functionType.parameters.index + argIndex];
					if (argType != paramType)
					{
						compiler.AddSoftError(
							compiler.parser.previousToken.slice,
							"Wrong type for argument {0}. Expected {1}. Got {2}",
							argIndex + 1,
							paramType.ToString(compiler.chunk),
							argType.ToString(compiler.chunk)
						);
					}
				}

				argIndex += 1;
			} while (compiler.parser.Match(TokenKind.Comma));
		}

		compiler.parser.Consume(TokenKind.CloseParenthesis, "Expect ')' after function argument list");

		if (hasFunction && argIndex != functionType.parameters.length)
			compiler.AddSoftError(slice, "Wrong number of arguments. Expected {0}. Got {1}", functionType.parameters.length, argIndex);

		compiler.EmitInstruction(Instruction.Call);
		compiler.EmitByte((byte)(hasFunction ? functionType.parameters.length : 0));
		compiler.typeStack.PushBack(
			hasFunction ? functionType.returnType : ValueType.Unit
		);
	}

	public void Unary(Precedence precedence)
	{
		var opToken = compiler.parser.previousToken;

		ParseWithPrecedence(Precedence.Unary);
		var type = compiler.typeStack.PopLast();

		switch ((TokenKind)opToken.kind)
		{
		case TokenKind.Minus:
			switch (type)
			{
			case ValueType.Int:
				compiler.EmitInstruction(Instruction.NegateInt);
				compiler.typeStack.PushBack(ValueType.Int);
				break;
			case ValueType.Float:
				compiler.EmitInstruction(Instruction.NegateFloat);
				compiler.typeStack.PushBack(ValueType.Float);
				break;
			default:
				compiler.AddSoftError(opToken.slice, "Unary minus operator can only be applied to ints or floats");
				compiler.typeStack.PushBack(type);
				break;
			}
			break;
		case TokenKind.Bang:
			switch (type)
			{
			case ValueType.Bool:
				compiler.EmitInstruction(Instruction.Not);
				compiler.typeStack.PushBack(ValueType.Bool);
				break;
			case ValueType.Int:
			case ValueType.Float:
			case ValueType.String:
				compiler.EmitInstruction(Instruction.Pop);
				compiler.EmitInstruction(Instruction.LoadFalse);
				compiler.typeStack.PushBack(ValueType.Bool);
				break;
			default:
				compiler.AddSoftError(opToken.slice, "Not operator can only be applied to bools, ints, floats or strings");
				compiler.typeStack.PushBack(ValueType.Bool);
				break;
			}
			break;
		default:
			compiler.AddHardError(
					opToken.slice,
					string.Format("Expected unary operator. Got {0}", opToken.kind)
				);
			compiler.typeStack.PushBack(ValueType.Unit);
			break;
		}
	}

	public void Binary(Precedence precedence)
	{
		var opToken = compiler.parser.previousToken;

		var opPrecedence = parseRules[(int)opToken.kind].precedence;
		ParseWithPrecedence(opPrecedence + 1);

		var bType = compiler.typeStack.PopLast();
		var aType = compiler.typeStack.PopLast();

		switch ((TokenKind)opToken.kind)
		{
		case TokenKind.Plus:
			if (aType == ValueType.Int && bType == ValueType.Int)
				compiler.EmitInstruction(Instruction.AddInt).typeStack.PushBack(ValueType.Int);
			else if (aType == ValueType.Float && bType == ValueType.Float)
				compiler.EmitInstruction(Instruction.AddFloat).typeStack.PushBack(ValueType.Float);
			else
				compiler.AddSoftError(opToken.slice, "Plus operator can only be applied to ints or floats").typeStack.PushBack(aType);
			break;
		case TokenKind.Minus:
			if (aType == ValueType.Int && bType == ValueType.Int)
				compiler.EmitInstruction(Instruction.SubtractInt).typeStack.PushBack(ValueType.Int);
			else if (aType == ValueType.Float && bType == ValueType.Float)
				compiler.EmitInstruction(Instruction.SubtractFloat).typeStack.PushBack(ValueType.Float);
			else
				compiler.AddSoftError(opToken.slice, "Minus operator can only be applied to ints or floats").typeStack.PushBack(aType);
			break;
		case TokenKind.Asterisk:
			if (aType == ValueType.Int && bType == ValueType.Int)
				compiler.EmitInstruction(Instruction.MultiplyInt).typeStack.PushBack(ValueType.Int);
			else if (aType == ValueType.Float && bType == ValueType.Float)
				compiler.EmitInstruction(Instruction.MultiplyFloat).typeStack.PushBack(ValueType.Float);
			else
				compiler.AddSoftError(opToken.slice, "Multiply operator can only be applied to ints or floats").typeStack.PushBack(aType);
			break;
		case TokenKind.Slash:
			if (aType == ValueType.Int && bType == ValueType.Int)
				compiler.EmitInstruction(Instruction.DivideInt).typeStack.PushBack(ValueType.Int);
			else if (aType == ValueType.Float && bType == ValueType.Float)
				compiler.EmitInstruction(Instruction.DivideFloat).typeStack.PushBack(ValueType.Float);
			else
				compiler.AddSoftError(opToken.slice, "Divide operator can only be applied to ints or floats").typeStack.PushBack(aType);
			break;
		case TokenKind.EqualEqual:
			if (aType != bType)
			{
				compiler.AddSoftError(opToken.slice, "Equal operator can only be applied to same type values");
				compiler.typeStack.PushBack(ValueType.Bool);
				break;
			}

			switch (aType)
			{
			case ValueType.Bool:
				compiler.EmitInstruction(Instruction.EqualBool);
				break;
			case ValueType.Int:
				compiler.EmitInstruction(Instruction.EqualInt);
				break;
			case ValueType.Float:
				compiler.EmitInstruction(Instruction.EqualFloat);
				break;
			case ValueType.String:
				compiler.EmitInstruction(Instruction.EqualString);
				break;
			default:
				compiler.AddSoftError(opToken.slice, "Equal operator can only be applied to bools, ints and floats");
				break;
			}
			compiler.typeStack.PushBack(ValueType.Bool);
			break;
		case TokenKind.BangEqual:
			if (aType != bType)
			{
				compiler.AddSoftError(opToken.slice, "NotEqual operator can only be applied to same type values");
				compiler.typeStack.PushBack(ValueType.Bool);
				break;
			}

			switch (aType)
			{
			case ValueType.Bool:
				compiler.EmitInstruction(Instruction.EqualBool);
				break;
			case ValueType.Int:
				compiler.EmitInstruction(Instruction.EqualInt);
				break;
			case ValueType.Float:
				compiler.EmitInstruction(Instruction.EqualFloat);
				break;
			case ValueType.String:
				compiler.EmitInstruction(Instruction.EqualString);
				break;
			default:
				compiler.AddSoftError(opToken.slice, "NotEqual operator can only be applied to bools, ints and floats");
				break;
			}
			compiler.EmitInstruction(Instruction.Not);
			compiler.typeStack.PushBack(ValueType.Bool);
			break;
		case TokenKind.Greater:
			if (aType == ValueType.Int && bType == ValueType.Int)
				compiler.EmitInstruction(Instruction.GreaterInt);
			else if (aType == ValueType.Float && bType == ValueType.Float)
				compiler.EmitInstruction(Instruction.GreaterFloat);
			else
				compiler.AddSoftError(opToken.slice, "Greater operator can only be applied to ints or floats");
			compiler.typeStack.PushBack(ValueType.Bool);
			break;
		case TokenKind.GreaterEqual:
			if (aType == ValueType.Int && bType == ValueType.Int)
				compiler
					.EmitInstruction(Instruction.LessInt)
					.EmitInstruction(Instruction.Not);
			else if (aType == ValueType.Float && bType == ValueType.Float)
				compiler
					.EmitInstruction(Instruction.LessFloat)
					.EmitInstruction(Instruction.Not);
			else
				compiler.AddSoftError(opToken.slice, "GreaterOrEqual operator can only be applied to ints or floats");
			compiler.typeStack.PushBack(ValueType.Bool);
			break;
		case TokenKind.Less:
			if (aType == ValueType.Int && bType == ValueType.Int)
				compiler.EmitInstruction(Instruction.LessInt);
			else if (aType == ValueType.Float && bType == ValueType.Float)
				compiler.EmitInstruction(Instruction.LessFloat);
			else
				compiler.AddSoftError(opToken.slice, "Less operator can only be applied to ints or floats");
			compiler.typeStack.PushBack(ValueType.Bool);
			break;
		case TokenKind.LessEqual:
			if (aType == ValueType.Int && bType == ValueType.Int)
				compiler
					.EmitInstruction(Instruction.GreaterInt)
					.EmitInstruction(Instruction.Not);
			else if (aType == ValueType.Float && bType == ValueType.Float)
				compiler
					.EmitInstruction(Instruction.GreaterFloat)
					.EmitInstruction(Instruction.Not);
			else
				compiler.AddSoftError(opToken.slice, "LessOrEqual operator can only be applied to ints or floats");
			compiler.typeStack.PushBack(ValueType.Bool);
			break;
		default:
			return;
		}
	}
}