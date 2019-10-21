public interface IStruct
{
	void Marshal<M>(ref M marshaler) where M : IMarshaler;
}

public interface IMarshalable
{
	void Marshal<M>(ref M marshaler) where M : IMarshaler;
	ValueType GetType(ByteCodeChunk chunk);
}

internal static class Marshal
{
	public sealed class InvalidReflectionException : System.Exception { }
	public sealed class InvalidDefinitionException : System.Exception { }

	internal readonly struct ReflectionData
	{
		public readonly ValueType type;

		public ReflectionData(ValueType type)
		{
			this.type = type;
		}
	}

	internal static class SizeOf<T> where T : struct, IMarshalable
	{
		public static byte size;
	}

	public static ValueType TypeOf<T>(ByteCodeChunk chunk) where T : struct, IMarshalable
	{
		return default(T).GetType(chunk);
	}
}

public interface IMarshaler
{
	VirtualMachine VirtualMachine { get; }

	void Marshal(string name);
	void Marshal(ref bool value, string name);
	void Marshal(ref int value, string name);
	void Marshal(ref float value, string name);
	void Marshal(ref string value, string name);
	void Marshal<T>(ref T value, string name) where T : struct, IMarshalable;
	void MarshalObject(ref object value);
}

internal interface IDefinitionMarshaler
{
	ValueType GetDefinedType();
}

internal struct MemoryReadMarshaler : IMarshaler
{
	private VirtualMachine vm;
	private int index;

	public MemoryReadMarshaler(VirtualMachine vm, int index)
	{
		this.vm = vm;
		this.index = index;
	}

	public VirtualMachine VirtualMachine { get { return vm; } }

	public void Marshal(string name) => index++;
	public void Marshal(ref bool value, string name) => value = vm.memory.values[index++].asBool;
	public void Marshal(ref int value, string name) => value = vm.memory.values[index++].asInt;
	public void Marshal(ref float value, string name) => value = vm.memory.values[index++].asFloat;
	public void Marshal(ref string value, string name) => value = vm.nativeObjects.buffer[vm.memory.values[index++].asInt] as string;
	public void Marshal<T>(ref T value, string name) where T : struct, IMarshalable => value.Marshal(ref this);
	public void MarshalObject(ref object value) => value = vm.nativeObjects.buffer[vm.memory.values[index++].asInt];
}

internal struct MemoryWriteMarshaler : IMarshaler
{
	private VirtualMachine vm;
	private int index;

	public MemoryWriteMarshaler(VirtualMachine vm, int index)
	{
		this.vm = vm;
		this.index = index;
	}

	public VirtualMachine VirtualMachine { get { return vm; } }

	public void Marshal(string name) => index++;
	public void Marshal(ref bool value, string name) => vm.memory.values[index++].asBool = value;
	public void Marshal(ref int value, string name) => vm.memory.values[index++].asInt = value;
	public void Marshal(ref float value, string name) => vm.memory.values[index++].asFloat = value;
	public void Marshal(ref string value, string name)
	{
		vm.memory.values[index++].asInt = vm.nativeObjects.count;
		vm.nativeObjects.PushBack(value);
	}
	public void Marshal<T>(ref T value, string name) where T : struct, IMarshalable => value.Marshal(ref this);
	public void MarshalObject(ref object value)
	{
		vm.memory.values[index++].asInt = vm.nativeObjects.count;
		vm.nativeObjects.PushBack(value);
	}
}

internal struct TupleDefinitionMarshaler<A> : IMarshaler, IDefinitionMarshaler where A : struct, IMarshalable
{
	internal ByteCodeChunk chunk;
	internal TupleTypeBuilder builder;

	public TupleDefinitionMarshaler(ByteCodeChunk chunk)
	{
		this.chunk = chunk;
		this.builder = chunk.BeginTupleType();
	}

	public VirtualMachine VirtualMachine { get { return null; } }

	public void Marshal(string name) => builder.WithElement(new ValueType(TypeKind.Unit));
	public void Marshal(ref bool value, string name) => builder.WithElement(new ValueType(TypeKind.Bool));
	public void Marshal(ref int value, string name) => builder.WithElement(new ValueType(TypeKind.Int));
	public void Marshal(ref float value, string name) => builder.WithElement(new ValueType(TypeKind.Float));
	public void Marshal(ref string value, string name) => builder.WithElement(new ValueType(TypeKind.String));
	public void Marshal<B>(ref B value, string name) where B : struct, IMarshalable => builder.WithElement(global::Marshal.TypeOf<B>(chunk));
	public void MarshalObject(ref object value) => throw new Marshal.InvalidDefinitionException();

	public ValueType GetDefinedType()
	{
		default(A).Marshal(ref this);
		var result = builder.Build(out var typeIndex);
		if (result == TupleTypeBuilder.Result.Success)
		{
			global::Marshal.SizeOf<A>.size = chunk.tupleTypes.buffer[typeIndex].size;
			return new ValueType(TypeKind.Tuple, typeIndex);
		}

		throw new Marshal.InvalidReflectionException();
	}
}

internal struct StructDefinitionMarshaler<A> : IMarshaler, IDefinitionMarshaler where A : struct, IStruct
{
	internal ByteCodeChunk chunk;
	internal StructTypeBuilder builder;

	public StructDefinitionMarshaler(ByteCodeChunk chunk)
	{
		this.chunk = chunk;
		this.builder = chunk.BeginStructType();
	}

	public VirtualMachine VirtualMachine { get { return null; } }

	public void Marshal(string name) => builder.WithField(name, new ValueType(TypeKind.Unit));
	public void Marshal(ref bool value, string name) => builder.WithField(name, new ValueType(TypeKind.Bool));
	public void Marshal(ref int value, string name) => builder.WithField(name, new ValueType(TypeKind.Int));
	public void Marshal(ref float value, string name) => builder.WithField(name, new ValueType(TypeKind.Float));
	public void Marshal(ref string value, string name) => builder.WithField(name, new ValueType(TypeKind.String));
	public void Marshal<B>(ref B value, string name) where B : struct, IMarshalable => builder.WithField(name, global::Marshal.TypeOf<B>(chunk));
	public void MarshalObject(ref object value) => throw new Marshal.InvalidDefinitionException();

	public ValueType GetDefinedType()
	{
		default(A).Marshal(ref this);
		var result = builder.Build(typeof(A).Name, out var typeIndex);
		if (
			result == StructTypeBuilder.Result.Success ||
			(
				result == StructTypeBuilder.Result.DuplicatedName &&
				global::Marshal.SizeOf<Struct<A>>.size > 0
			)
		)
		{
			global::Marshal.SizeOf<Struct<A>>.size = chunk.structTypes.buffer[typeIndex].size;
			return new ValueType(TypeKind.Struct, typeIndex);
		}

		throw new Marshal.InvalidReflectionException();
	}
}

internal struct FunctionDefinitionMarshaler<A, R> : IMarshaler, IDefinitionMarshaler
	where A : struct, ITuple
	where R : struct, IMarshalable
{
	internal ByteCodeChunk chunk;
	internal FunctionTypeBuilder builder;

	public FunctionDefinitionMarshaler(ByteCodeChunk chunk)
	{
		this.chunk = chunk;
		this.builder = chunk.BeginFunctionType();
	}

	public VirtualMachine VirtualMachine { get { return null; } }

	public void Marshal(string name) => builder.WithParam(new ValueType(TypeKind.Unit));
	public void Marshal(ref bool value, string name) => builder.WithParam(new ValueType(TypeKind.Bool));
	public void Marshal(ref int value, string name) => builder.WithParam(new ValueType(TypeKind.Int));
	public void Marshal(ref float value, string name) => builder.WithParam(new ValueType(TypeKind.Float));
	public void Marshal(ref string value, string name) => builder.WithParam(new ValueType(TypeKind.String));
	public void Marshal<M>(ref M value, string name) where M : struct, IMarshalable => builder.WithParam(global::Marshal.TypeOf<M>(chunk));
	public void MarshalObject(ref object value) => throw new Marshal.InvalidDefinitionException();

	public void Returns()
	{
		builder.returnType = global::Marshal.TypeOf<R>(chunk);
	}

	public ValueType GetDefinedType()
	{
		default(A).Marshal(ref this);
		var result = builder.Build(out var typeIndex);
		if (result == FunctionTypeBuilder.Result.Success)
			return new ValueType(TypeKind.Function, typeIndex);

		throw new Marshal.InvalidReflectionException();
	}
}
