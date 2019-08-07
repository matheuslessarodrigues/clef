public static class CompilerFlowExtensions
{
	public static Scope BeginScope(this Compiler self)
	{
		self.scopeDepth += 1;
		return new Scope(self.localVariables.count);
	}

	public static void EndScope(this Compiler self, Scope scope, int sizeLeftOnStack)
	{
		self.scopeDepth -= 1;

		for (var i = scope.localVariablesStartIndex; i < self.localVariables.count; i++)
		{
			var variable = self.localVariables.buffer[i];
			if (!variable.isUsed)
				self.AddSoftError(variable.slice, "Unused variable");
		}

		var localCount = self.localVariables.count - scope.localVariablesStartIndex;
		if (localCount == 0)
			return;

		var localVarsSize = 0;
		for (var i = scope.localVariablesStartIndex; i < self.localVariables.count; i++)
		{
			var type = self.localVariables.buffer[i].type;
			localVarsSize += self.chunk.GetTypeSize(type);
		}

		if (sizeLeftOnStack > 0)
		{
			self.EmitInstruction(Instruction.Move);
			self.EmitByte((byte)localVarsSize);
			self.EmitByte((byte)sizeLeftOnStack);
		}
		else
		{
			self.EmitInstruction(Instruction.PopMultiple);
			self.EmitByte((byte)localVarsSize);
		}

		self.localVariables.count -= localCount;
		self.typeStack.count -= localCount;
	}

	public static void BeginLoop(this Compiler self)
	{
		self.loopNesting += 1;
	}

	public static void EndLoop(this Compiler self)
	{
		self.loopNesting -= 1;

		for (var i = self.loopBreaks.count - 1; i >= 0; i--)
		{
			var loopBreak = self.loopBreaks.buffer[i];
			if (loopBreak.nesting == self.loopNesting)
			{
				self.EndEmitForwardJump(loopBreak.jump);
				self.loopBreaks.SwapRemove(i);
			}
		}
	}

	public static bool BreakLoop(this Compiler self, int nesting, int jump)
	{
		if (self.loopNesting < nesting)
			return false;

		self.loopBreaks.PushBack(new LoopBreak(self.loopNesting - nesting, jump));
		return true;
	}
}