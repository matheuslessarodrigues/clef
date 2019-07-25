﻿using System.Collections.Generic;
using System.Text;

public readonly struct LineAndColumn
{
	public readonly int line;
	public readonly int column;

	public LineAndColumn(int line, int column)
	{
		this.line = line;
		this.column = column;
	}

	public override string ToString()
	{
		return string.Format("line {0} column: {1}", line, column);
	}
}

public static class ParserHelper
{
	public static int ToInteger(string source, Token token)
	{
		var sub = source.Substring(token.index, token.length);
		return int.Parse(sub);
	}

	public static float ToFloat(string source, Token token)
	{
		var sub = source.Substring(token.index, token.length);
		return float.Parse(sub);
	}

	public static LineAndColumn GetLineAndColumn(string source, int index)
	{
		var line = 1;
		var lastNewLineIndex = 0;

		for (var i = 0; i < index; i++)
		{
			if (source[i] == '\n')
			{
				lastNewLineIndex = i;
				line += 1;
			}
		}

		return new LineAndColumn(line, index - lastNewLineIndex + 1);
	}

	public static string GetLines(string source, int startLine, int endLine)
	{
		var startLineIndex = 0;
		var lineCount = 0;

		for (var i = 0; i < source.Length; i++)
		{
			if (source[i] != '\n')
				continue;

			if (lineCount == endLine)
				return source.Substring(startLineIndex, i - startLineIndex);

			lineCount += 1;

			if (lineCount == startLine)
				startLineIndex = i + 1;
		}

		if (lineCount >= startLine && lineCount <= endLine)
			return source.Substring(startLineIndex);

		return "";
	}

	public static string FormatError(string source, List<ParseError> errors, int contextSize)
	{
		if (errors == null)
			return "";

		var sb = new StringBuilder();

		foreach (var e in errors)
		{
			var position = GetLineAndColumn(source, e.sourceIndex);

			sb.Append(e.message);
			sb.Append(" (line: ");
			sb.Append(position.line);
			sb.Append(", column: ");
			sb.Append(position.column);
			sb.AppendLine(")");

			sb.Append(ParserHelper.GetLines(
				source,
				System.Math.Max(position.line - 1 - contextSize, 0),
				System.Math.Max(position.line - 1, 0)
			));
			sb.AppendLine();
			sb.Append(' ', position.column - 1);
			sb.Append("^ here\n");
		}

		return sb.ToString();
	}
}
