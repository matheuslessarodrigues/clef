using System.Diagnostics;

[DebuggerTypeProxy(typeof(ByteCodeChunkDebugView))]
public sealed class ByteCodeChunk
{
	public readonly struct Function
	{
		public readonly string name;
		public readonly int codeIndex;
		public readonly Buffer<ValueType> paramTypes;
		public readonly ValueType returnType;

		public Function(string name, int codeIndex, Buffer<ValueType> paramTypes, ValueType returnType)
		{
			this.name = name;
			this.codeIndex = codeIndex;
			this.paramTypes = paramTypes;
			this.returnType = returnType;
		}
	}

	public Buffer<byte> bytes = new Buffer<byte>(256);
	public Buffer<Slice> slices = new Buffer<Slice>(256);
	public Buffer<ValueData> literalData = new Buffer<ValueData>(64);
	public Buffer<ValueType> literalTypes = new Buffer<ValueType>(64);
	public Buffer<string> stringLiterals = new Buffer<string>(16);
	public Buffer<Function> functions = new Buffer<Function>(64);

	public int AddValueLiteral(ValueData value, ValueType type)
	{
		var index = FindValueIndex(value, type);
		if (index < 0)
		{
			index = literalData.count;
			literalData.PushBack(value);
			literalTypes.PushBack(type);
		}

		return index;
	}

	public int AddStringLiteral(string literal)
	{
		var stringIndex = System.Array.IndexOf(stringLiterals.buffer, literal);
		if (stringIndex < 0)
		{
			stringIndex = stringLiterals.count;
			stringLiterals.PushBack(literal);
		}

		return AddValueLiteral(new ValueData(stringIndex), ValueType.String);
	}

	public void WriteByte(byte value, Slice slice)
	{
		bytes.PushBack(value);
		slices.PushBack(slice);
	}

	private int FindValueIndex(ValueData value, ValueType type)
	{
		for (var i = 0; i < literalData.count; i++)
		{
			if (type != literalTypes.buffer[i])
				continue;

			var v = literalData.buffer[i];
			switch (type)
			{
			case ValueType.Bool:
				if (v.asBool == value.asBool)
					return i;
				break;
			case ValueType.Int:
				if (v.asInt == value.asInt)
					return i;
				break;
			case ValueType.Float:
				if (v.asFloat == value.asFloat)
					return i;
				break;
			default: break;
			}
		}

		return -1;
	}
}