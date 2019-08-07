public static class PepperCompilerHelper
{
	public static void ParseWithPrecedence(Compiler compiler, Precedence precedence)
	{
		var parser = compiler.common.parser;
		parser.Next();
		if (parser.previousToken.kind == TokenKind.End)
			return;

		var prefixRule = compiler.parseRules[(int)parser.previousToken.kind].prefixRule;
		if (prefixRule == null)
		{
			compiler.common.AddHardError(parser.previousToken.slice, "Expected expression");
			return;
		}
		prefixRule(precedence);

		while (
			parser.currentToken.kind != TokenKind.End &&
			precedence <= compiler.parseRules[(int)parser.currentToken.kind].precedence
		)
		{
			parser.Next();
			var infixRule = compiler.parseRules[(int)parser.previousToken.kind].infixRule;
			infixRule(precedence);
		}

		var canAssign = precedence <= Precedence.Assignment;
		if (canAssign && compiler.common.parser.Match(TokenKind.Equal))
		{
			compiler.common.AddHardError(compiler.common.parser.previousToken.slice, "Invalid assignment target");
			compiler.Expression();
		}
	}

	public static void BeginLoop(Compiler lang)
	{
		lang.loopNesting += 1;
	}

	public static void EndLoop(Compiler lang, CompilerCommon compiler)
	{
		lang.loopNesting -= 1;

		for (var i = lang.loopBreaks.count - 1; i >= 0; i--)
		{
			var loopBreak = lang.loopBreaks.buffer[i];
			if (loopBreak.nesting == lang.loopNesting)
			{
				compiler.EndEmitForwardJump(loopBreak.jump);
				lang.loopBreaks.SwapRemove(i);
			}
		}
	}

	public static bool BreakLoop(Compiler lang, int nesting, int jump)
	{
		if (lang.loopNesting < nesting)
			return false;

		lang.loopBreaks.PushBack(new LoopBreak(lang.loopNesting - nesting, jump));
		return true;
	}

	public static ValueType ConsumeType(this Compiler lang, CompilerCommon compiler, string error, int recursionLevel)
	{
		if (recursionLevel > 8)
		{
			compiler.AddSoftError(compiler.parser.previousToken.slice, "Type is nested too deeply");
			return ValueType.Unit;
		}

		var type = new Option<ValueType>();

		if (compiler.parser.Match(TokenKind.Bool))
			type = Option.Some(ValueType.Bool);
		else if (compiler.parser.Match(TokenKind.Int))
			type = Option.Some(ValueType.Int);
		else if (compiler.parser.Match(TokenKind.Float))
			type = Option.Some(ValueType.Float);
		else if (compiler.parser.Match(TokenKind.String))
			type = Option.Some(ValueType.String);
		else if (compiler.parser.Match(TokenKind.Identifier))
			type = lang.ResolveStructType(compiler, recursionLevel + 1);
		else if (compiler.parser.Match(TokenKind.Function))
			type = lang.ResolveFunctionType(compiler, recursionLevel + 1);

		if (type.isSome)
			return type.value;

		compiler.AddSoftError(compiler.parser.previousToken.slice, error);
		return ValueType.Unit;
	}

	private static Option<ValueType> ResolveStructType(this Compiler lang, CompilerCommon compiler, int recursionLevel)
	{
		var source = compiler.parser.tokenizer.source;
		var slice = compiler.parser.previousToken.slice;

		for (var i = 0; i < compiler.chunk.structTypes.count; i++)
		{
			var structName = compiler.chunk.structTypes.buffer[i].name;
			if (CompilerHelper.AreEqual(source, slice, structName))
				return Option.Some(ValueTypeHelper.SetIndex(ValueType.String, i));
		}

		return Option.None;
	}

	private static Option<ValueType> ResolveFunctionType(this Compiler lang, CompilerCommon compiler, int recursionLevel)
	{
		var declaration = compiler.chunk.BeginAddFunctionType();

		compiler.parser.Consume(TokenKind.OpenParenthesis, "Expected '(' after function type");
		if (!compiler.parser.Check(TokenKind.CloseParenthesis))
		{
			do
			{
				var paramType = lang.ConsumeType(compiler, "Expected function parameter type", recursionLevel);
				declaration.AddParam(paramType);
			} while (compiler.parser.Match(TokenKind.Comma));
		}
		compiler.parser.Consume(TokenKind.CloseParenthesis, "Expected ')' after function type parameter list");
		if (compiler.parser.Match(TokenKind.Colon))
			declaration.returnType = lang.ConsumeType(compiler, "Expected function return type", recursionLevel);

		var functionTypeIndex = compiler.chunk.EndAddFunctionType(declaration);
		var type = ValueTypeHelper.SetIndex(ValueType.Function, functionTypeIndex);

		return Option.Some(type);
	}
}