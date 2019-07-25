public static class LangParseRules
{
	public static readonly ParseRule[] rules = new ParseRule[(int)TokenKind.COUNT];

	public static void InitRules()
	{
		Set(TokenKind.OpenParenthesis, LangCompiler.Grouping, null, Precedence.Call);
		Set(TokenKind.CloseParenthesis, null, null, Precedence.None);
		Set(TokenKind.OpenCurlyBrackets, null, null, Precedence.None);
		Set(TokenKind.CloseCurlyBrackets, null, null, Precedence.None);
		Set(TokenKind.Comma, null, null, Precedence.None);
		Set(TokenKind.Dot, null, null, Precedence.Call);
		Set(TokenKind.Minus, LangCompiler.Unary, LangCompiler.Binary, Precedence.Term);
		Set(TokenKind.Plus, null, LangCompiler.Binary, Precedence.Term);
		Set(TokenKind.Semicolon, null, null, Precedence.None);
		Set(TokenKind.Slash, null, LangCompiler.Binary, Precedence.Factor);
		Set(TokenKind.Asterisk, null, LangCompiler.Binary, Precedence.Factor);
		Set(TokenKind.Bang, null, null, Precedence.None);
		Set(TokenKind.BangEqual, null, null, Precedence.Equality);
		Set(TokenKind.Equal, null, null, Precedence.None);
		Set(TokenKind.EqualEqual, null, null, Precedence.Equality);
		Set(TokenKind.Greater, null, null, Precedence.Comparison);
		Set(TokenKind.GreaterEqual, null, null, Precedence.Comparison);
		Set(TokenKind.Less, null, null, Precedence.Comparison);
		Set(TokenKind.LessEqual, null, null, Precedence.Comparison);
		Set(TokenKind.Identifier, null, null, Precedence.None);
		Set(TokenKind.String, null, null, Precedence.None);
		Set(TokenKind.IntegerNumber, LangCompiler.Number, null, Precedence.None);
		Set(TokenKind.And, null, null, Precedence.And);
		Set(TokenKind.Else, null, null, Precedence.None);
		Set(TokenKind.False, LangCompiler.Literal, null, Precedence.None);
		Set(TokenKind.For, null, null, Precedence.None);
		Set(TokenKind.Function, null, null, Precedence.None);
		Set(TokenKind.If, null, null, Precedence.None);
		Set(TokenKind.Nil, LangCompiler.Literal, null, Precedence.None);
		Set(TokenKind.Or, null, null, Precedence.Or);
		Set(TokenKind.RealNumber, LangCompiler.Number, null, Precedence.None);
		Set(TokenKind.Return, null, null, Precedence.None);
		Set(TokenKind.True, LangCompiler.Literal, null, Precedence.None);
		Set(TokenKind.Let, null, null, Precedence.None);
		Set(TokenKind.While, null, null, Precedence.None);
	}

	private static void Set(TokenKind kind, ParseFunction prefix, ParseFunction infix, Precedence precedence)
	{
		rules[(int)kind] = new ParseRule(prefix, infix, (int)precedence);
	}
}