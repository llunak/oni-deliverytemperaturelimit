// ONI's Harmony is too old to have things like LocalLocal(), so copy&paste from Harmony.
/*
MIT License

Copyright (c) 2017 Andreas Pardeike

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
*/

using HarmonyLib;
using System;
using System.Reflection.Emit;

namespace DeliveryTemperatureLimit
{
    public static class CodeInstruction2
    {
        public static CodeInstruction LoadLocal(int index, bool useAddress = false)
        {
            if (useAddress)
            {
                if (index < 256) return new CodeInstruction(OpCodes.Ldloca_S, Convert.ToByte(index));
                else return new CodeInstruction(OpCodes.Ldloca, index);
            }
            else
            {
                if (index == 0) return new CodeInstruction(OpCodes.Ldloc_0);
                else if (index == 1) return new CodeInstruction(OpCodes.Ldloc_1);
                else if (index == 2) return new CodeInstruction(OpCodes.Ldloc_2);
                else if (index == 3) return new CodeInstruction(OpCodes.Ldloc_3);
                else if (index < 256) return new CodeInstruction(OpCodes.Ldloc_S, Convert.ToByte(index));
                else return new CodeInstruction(OpCodes.Ldloc, index);
            }
        }
        public static CodeInstruction StoreLocal(int index)
        {
            if (index == 0) return new CodeInstruction(OpCodes.Stloc_0);
            else if (index == 1) return new CodeInstruction(OpCodes.Stloc_1);
            else if (index == 2) return new CodeInstruction(OpCodes.Stloc_2);
            else if (index == 3) return new CodeInstruction(OpCodes.Stloc_3);
            else if (index < 256) return new CodeInstruction(OpCodes.Stloc_S, Convert.ToByte(index));
            else return new CodeInstruction(OpCodes.Stloc, index);
        }

        public static int LocalIndex(this CodeInstruction code)
        {
            if (code.opcode == OpCodes.Ldloc_0 || code.opcode == OpCodes.Stloc_0) return 0;
            else if (code.opcode == OpCodes.Ldloc_1 || code.opcode == OpCodes.Stloc_1) return 1;
            else if (code.opcode == OpCodes.Ldloc_2 || code.opcode == OpCodes.Stloc_2) return 2;
            else if (code.opcode == OpCodes.Ldloc_3 || code.opcode == OpCodes.Stloc_3) return 3;
            else if (code.opcode == OpCodes.Ldloc_S || code.opcode == OpCodes.Ldloc) return Convert.ToInt32(code.operand);
            else if (code.opcode == OpCodes.Stloc_S || code.opcode == OpCodes.Stloc) return /*Convert.ToInt32*/ToIndex(code.operand);
            else if (code.opcode == OpCodes.Ldloca_S || code.opcode == OpCodes.Ldloca) return Convert.ToInt32(code.operand);
            else throw new ArgumentException("Instruction is not a load or store", nameof(code));
        }

        public static int ToIndex(object operand) // The Convert.ToInt32() call does not work for some reason, handle it.
        {
            if( operand is LocalBuilder local )
                return local.LocalIndex;
            return Convert.ToInt32(operand);
        }
    }
}
