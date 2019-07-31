internal static class VirtualMachineInstructions
{
	public static byte NextByte(VirtualMachine vm, ref VirtualMachine.CallFrame frame)
	{
		return vm.chunk.bytes.buffer[frame.instructionIndex++];
	}

	public static bool Tick(VirtualMachine vm)
	{
		ref var frame = ref vm.callframeStack.buffer[vm.callframeStack.count - 1];

		var nextInstruction = (Instruction)NextByte(vm, ref frame);
		switch (nextInstruction)
		{
		case Instruction.Halt:
			return true;
		case Instruction.Return:
			System.Console.WriteLine(VirtualMachineHelper.PopToString(vm));
			return true;
		case Instruction.Print:
			System.Console.WriteLine(VirtualMachineHelper.PopToString(vm));
			break;
		case Instruction.Pop:
			vm.valueStack.count -= 1;
			vm.typeStack.count -= 1;
			break;
		case Instruction.PopMultiple:
			{
				var count = NextByte(vm, ref frame);
				vm.valueStack.count -= count;
				vm.typeStack.count -= count;
			}
			break;
		case Instruction.CopyTo:
			{
				var last = vm.valueStack.count - 1;
				var index = last - NextByte(vm, ref frame);
				vm.valueStack.buffer[index] = vm.valueStack.buffer[last];
				vm.typeStack.buffer[index] = vm.typeStack.buffer[last];
			}
			break;
		case Instruction.LoadNil:
			vm.PushValue(new ValueData(), ValueType.Nil);
			break;
		case Instruction.LoadFalse:
			vm.PushValue(new ValueData(false), ValueType.Bool);
			break;
		case Instruction.LoadTrue:
			vm.PushValue(new ValueData(true), ValueType.Bool);
			break;
		case Instruction.LoadLiteral:
			{
				var index = NextByte(vm, ref frame);
				vm.PushValue(
					vm.chunk.literalData.buffer[index],
					vm.chunk.literalTypes.buffer[index]
				);
				break;
			}
		case Instruction.AssignLocal:
			vm.valueStack.buffer[frame.baseIndex + NextByte(vm, ref frame)] = vm.Peek();
			break;
		case Instruction.LoadLocal:
			{
				var index = NextByte(vm, ref frame);
				vm.PushValue(
					vm.valueStack.buffer[frame.baseIndex + index],
					vm.typeStack.buffer[frame.baseIndex + index]
				);
				break;
			}
		case Instruction.IncrementLocal:
			{
				var index = NextByte(vm, ref frame);
				vm.valueStack.buffer[frame.baseIndex + index].asInt += 1;
				break;
			}
		case Instruction.IntToFloat:
			vm.PushValue(new ValueData((float)vm.PopValue().asInt), ValueType.Float);
			break;
		case Instruction.FloatToInt:
			vm.PushValue(new ValueData((int)vm.PopValue().asFloat), ValueType.Int);
			break;
		case Instruction.NegateInt:
			vm.Peek().asInt = -vm.Peek().asInt;
			break;
		case Instruction.NegateFloat:
			vm.Peek().asFloat = -vm.Peek().asFloat;
			break;
		case Instruction.AddInt:
			vm.PeekBefore().asInt += vm.PopValue().asInt;
			break;
		case Instruction.AddFloat:
			vm.PeekBefore().asFloat += vm.PopValue().asFloat;
			break;
		case Instruction.SubtractInt:
			vm.PeekBefore().asInt -= vm.PopValue().asInt;
			break;
		case Instruction.SubtractFloat:
			vm.PeekBefore().asFloat -= vm.PopValue().asFloat;
			break;
		case Instruction.MultiplyInt:
			vm.PeekBefore().asInt *= vm.PopValue().asInt;
			break;
		case Instruction.MultiplyFloat:
			vm.PeekBefore().asFloat *= vm.PopValue().asFloat;
			break;
		case Instruction.DivideInt:
			vm.PeekBefore().asInt /= vm.PopValue().asInt;
			break;
		case Instruction.DivideFloat:
			vm.PeekBefore().asFloat /= vm.PopValue().asFloat;
			break;
		case Instruction.Not:
			vm.Peek().asBool = !vm.Peek().asBool;
			break;
		case Instruction.EqualBool:
			vm.PushValue(
				new ValueData(vm.PopValue().asBool == vm.PopValue().asBool),
				ValueType.Bool
			);
			break;
		case Instruction.EqualInt:
			vm.PushValue(
				new ValueData(vm.PopValue().asInt == vm.PopValue().asInt),
				ValueType.Bool
			);
			break;
		case Instruction.EqualFloat:
			vm.PushValue(
				new ValueData(vm.PopValue().asFloat == vm.PopValue().asFloat),
				ValueType.Bool
			);
			break;
		case Instruction.EqualString:
			vm.PushValue(
				new ValueData(
					(vm.heap.buffer[vm.PopValue().asInt] as string).Equals(
					vm.heap.buffer[vm.PopValue().asInt] as string)
				),
				ValueType.Bool
			);
			break;
		case Instruction.GreaterInt:
			vm.PushValue(
				new ValueData(vm.PopValue().asInt < vm.PopValue().asInt),
				ValueType.Bool
			);
			break;
		case Instruction.GreaterFloat:
			vm.PushValue(
				new ValueData(vm.PopValue().asFloat < vm.PopValue().asFloat),
				ValueType.Bool
			);
			break;
		case Instruction.LessInt:
			vm.PushValue(
				new ValueData(vm.PopValue().asInt > vm.PopValue().asInt),
				ValueType.Bool
			);
			break;
		case Instruction.LessFloat:
			vm.PushValue(
				new ValueData(vm.PopValue().asFloat > vm.PopValue().asFloat),
				ValueType.Bool
			);
			break;
		case Instruction.JumpForward:
			{
				var offset = BytesHelper.BytesToShort(NextByte(vm, ref frame), NextByte(vm, ref frame));
				frame.instructionIndex += offset;
				break;
			}
		case Instruction.JumpBackward:
			{
				var offset = BytesHelper.BytesToShort(NextByte(vm, ref frame), NextByte(vm, ref frame));
				frame.instructionIndex -= offset;
				break;
			}
		case Instruction.JumpForwardIfFalse:
			{
				var offset = BytesHelper.BytesToShort(NextByte(vm, ref frame), NextByte(vm, ref frame));
				if (!vm.valueStack.buffer[vm.valueStack.count - 1].asBool)
					frame.instructionIndex += offset;
				break;
			}
		case Instruction.JumpForwardIfTrue:
			{
				var offset = BytesHelper.BytesToShort(NextByte(vm, ref frame), NextByte(vm, ref frame));
				if (vm.valueStack.buffer[vm.valueStack.count - 1].asBool)
					frame.instructionIndex += offset;
				break;
			}
		case Instruction.PopAndJumpForwardIfFalse:
			{
				var offset = BytesHelper.BytesToShort(NextByte(vm, ref frame), NextByte(vm, ref frame));
				if (!vm.PopValue().asBool)
					frame.instructionIndex += offset;
				break;
			}
		case Instruction.ForLoopCheck:
			{
				var index = NextByte(vm, ref frame);
				var less = vm.valueStack.buffer[frame.baseIndex + index].asInt < vm.valueStack.buffer[frame.baseIndex + index + 1].asInt;
				vm.PushValue(
					new ValueData(less),
					ValueType.Bool
				);
				break;
			}
		default:
			break;
		}

		return false;
	}
}