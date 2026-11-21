using UndertaleModLib.Models;
using static UndertaleModLib.Models.UndertaleInstruction;

namespace Jamm.ModMaker;

// Inserts stack-neutral calls without recompiling the game code. Offsets in GM1 are
// relative to the branch instruction, in words, including the two with-context jumps.
internal static class LegacyCodeHooks
{
    internal readonly record struct Hook(UndertaleFunction? Before = null, UndertaleFunction? After = null,
        UndertaleFunction? ReplaceCall = null);

    internal static bool Supports(UndertaleCode code)
    {
        if (code.ParentEntry is not null || code.Offset != 0) return false;
        var addresses = new HashSet<int>();
        var targets = new List<int>();
        int address = 0;
        foreach (var instruction in code.Instructions)
        {
            addresses.Add(address);
            if (HasBranchTarget(instruction)) targets.Add(address + instruction.JumpOffset);
            address += (int)instruction.CalculateInstructionSize();
        }
        addresses.Add(address);
        return targets.All(addresses.Contains);
    }

    private static bool HasBranchTarget(UndertaleInstruction instruction)
        => GetInstructionType(instruction.Kind) == InstructionType.GotoInstruction
            && !(instruction.Kind == Opcode.PopEnv && instruction.JumpOffsetPopenvExitMagic);

    internal static bool Insert(UndertaleCode code, Func<UndertaleInstruction, Hook> select,
        UndertaleFunction? enter = null, UndertaleFunction? leave = null)
    {
        var edits = code.Instructions.Select(select).ToArray();
        if (enter is null && leave is null && edits.All(hook => hook == default)) return false;

        var replacement = new List<UndertaleInstruction>(code.Instructions.Count);
        var destinations = new Dictionary<int, int>();
        var branches = new List<(UndertaleInstruction Branch, int Origin, int Destination)>();
        int oldAddress = 0, newAddress = 0;
        AddCall(enter);
        for (int i = 0; i < edits.Length; i++)
        {
            var instruction = code.Instructions[i];
            var edit = edits[i];
            destinations.Add(oldAddress, newAddress);
            AddCall(edit.Before);
            if (instruction.Kind is Opcode.Ret or Opcode.Exit) AddCall(leave);
            if (HasBranchTarget(instruction))
                branches.Add((instruction, newAddress, oldAddress + instruction.JumpOffset));
            if (edit.ReplaceCall is not null)
            {
                // A legacy variable load and a static call occupy the same two words
                // and both leave a variable value on the stack.
                if (instruction.Kind != Opcode.Call)
                {
                    instruction.Kind = Opcode.Call;
                    instruction.Type1 = DataType.Int32;
                    instruction.ArgumentsCount = 0;
                    instruction.ReferenceType = default;
                }
                instruction.ValueFunction = edit.ReplaceCall;
            }
            replacement.Add(instruction);
            int size = (int)instruction.CalculateInstructionSize();
            oldAddress += size;
            newAddress += size;
            AddCall(edit.After);
        }
        destinations.Add(oldAddress, newAddress);
        AddCall(leave);

        foreach (var (branch, origin, destination) in branches)
            branch.JumpOffset = destinations[destination] - origin;
        code.Instructions.Clear();
        code.Instructions.AddRange(replacement);
        code.UpdateLength();
        return true;

        void AddCall(UndertaleFunction? function)
        {
            if (function is null) return;
            replacement.Add(new UndertaleInstruction
            {
                Kind = Opcode.Call, Type1 = DataType.Int32, ValueFunction = function
            });
            replacement.Add(new UndertaleInstruction { Kind = Opcode.Popz, Type1 = DataType.Variable });
            newAddress += 3;
        }
    }
}
