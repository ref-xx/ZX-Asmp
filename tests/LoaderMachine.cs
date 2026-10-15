// Instruction-level check of the generated startup/restore paths, not a raster timing emulator.
using System;
using System.Linq;

internal sealed class LoaderMachine
{
    readonly byte[] memory = new byte[65536];
    byte[] r = { 0x12, 0x34, 0x56, 0x78, 0x9a, 0xbc, 0, 0xde };
    byte[] alt = { 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0, 0xef };
    byte flags = 0xa5, altFlags = 0x5a;
    int pc = 32768, sp = 32000, ix = 0x1357, iy = 0x2468;
    int Word(int p) { return memory[p] | memory[(p + 1) & 65535] << 8; }
    void Word(int p, int v) { memory[p] = (byte)v; memory[(p + 1) & 65535] = (byte)(v >> 8); }
    int Fetch() { int n = Word(pc); pc += 2; return n; }
    int Pair(int i) { return r[i] << 8 | r[i + 1]; }
    void Pair(int i, int n) { r[i] = (byte)(n >> 8); r[i + 1] = (byte)n; }
    void Push(int v) { sp = (sp - 2) & 65535; Word(sp, v); }
    int Pop() { int v = Word(sp); sp = (sp + 2) & 65535; return v; }
    bool Carry { get { return (flags & 1) != 0; } }
    bool Zero { get { return (flags & 64) != 0; } }
    void Flag(int value, bool carry) { flags = (byte)((value == 0 ? 64 : 0) | (carry ? 1 : 0)); }
    void RunTo(int target)
    {
        for (int steps = 0; steps < 2000000; steps++)
        {
            if (pc == target) return;
            int op = memory[pc++];
            if (op == 0x01 || op == 0x11 || op == 0x21) { Pair((op >> 4) * 2, Fetch()); continue; }
            if (op == 0xc5 || op == 0xd5 || op == 0xe5) { Push(Pair(((op >> 4) - 12) * 2)); continue; }
            if (op == 0xc1 || op == 0xd1 || op == 0xe1) { Pair(((op >> 4) - 12) * 2, Pop()); continue; }
            if (op == 0x18 || op == 0x20 || op == 0x30 || op == 0x38)
            {
                int displacement = (sbyte)memory[pc++];
                if (op == 0x18 || (op == 0x20 && !Zero) || (op == 0x30 && !Carry) || (op == 0x38 && Carry)) pc += displacement;
                continue;
            }
            switch (op)
            {
                case 0x76: case 0xf3: case 0xfb: break; // supply HALT's initial interrupt
                case 0x31: sp = Fetch(); break;
                case 0xf5: Push(r[7] << 8 | flags); break;
                case 0xf1: int af = Pop(); r[7] = (byte)(af >> 8); flags = (byte)af; break;
                case 0x08:
                    byte a = r[7]; r[7] = alt[7]; alt[7] = a;
                    byte f = flags; flags = altFlags; altFlags = f; break;
                case 0xd9:
                    for (int i = 0; i < 6; i++) { byte t = r[i]; r[i] = alt[i]; alt[i] = t; } break;
                case 0xdd: case 0xfd:
                    int indexOp = memory[pc++];
                    if (indexOp == 0xe5) Push(op == 0xdd ? ix : iy);
                    else if (indexOp == 0xe1) { int value = Pop(); if (op == 0xdd) ix = value; else iy = value; }
                    else throw new Exception("Unsupported index instruction.");
                    break;
                case 0xcd: case 0xd4:
                    int call = Fetch(); if (op == 0xcd || !Carry) { Push(pc); pc = call; } break;
                case 0xc3: pc = Fetch(); break;
                case 0xc9: pc = Pop(); break;
                case 0xc8: if (Zero) pc = Pop(); break;
                case 0xd8: if (Carry) pc = Pop(); break;
                case 0x03: Pair(0, Pair(0) + 1); break;
                case 0x23: Pair(4, Pair(4) + 1); break;
                case 0x0c: r[1]++; Flag(r[1], Carry); break;
                case 0x0e: r[1] = memory[pc++]; break;
                case 0x3e: r[7] = memory[pc++]; break;
                case 0x41: r[0] = r[1]; break;
                case 0x4e: r[1] = memory[Pair(4)]; break;
                case 0x7e: r[7] = memory[Pair(4)]; break;
                case 0x87: int sum = r[7] * 2; r[7] = (byte)sum; Flag(r[7], sum > 255); break;
                case 0x17:
                    int rotated = r[7] * 2 + (Carry ? 1 : 0);
                    r[7] = (byte)rotated; flags = (byte)((flags & ~1) | (rotated > 255 ? 1 : 0)); break;
                case 0x19:
                    int total = Pair(4) + Pair(2); Pair(4, total);
                    flags = (byte)((flags & ~1) | (total > 65535 ? 1 : 0)); break;
                case 0xe3: int hl = Pair(4); Pair(4, Word(sp)); Word(sp, hl); break;
                case 0xcb:
                    int cb = memory[pc++], reg = cb & 7, old = r[reg]; bool carry = Carry;
                    if ((cb & 0xf8) == 0x10) { r[reg] = (byte)(old * 2 + (carry ? 1 : 0)); Flag(r[reg], old >= 128); }
                    else if ((cb & 0xf8) == 0x18) { r[reg] = (byte)((old >> 1) | (carry ? 128 : 0)); Flag(r[reg], (old & 1) != 0); }
                    else throw new Exception("Unsupported CB instruction.");
                    break;
                case 0xed:
                    int ed = memory[pc++];
                    if (ed == 0x73) Word(Fetch(), sp);
                    else if (ed == 0x7b) sp = Word(Fetch());
                    else if (ed == 0x56) { }
                    else if (ed == 0xb0 || ed == 0xb8)
                    {
                        int direction = ed == 0xb0 ? 1 : -1;
                        do { memory[Pair(2)] = memory[Pair(4)]; Pair(2, Pair(2) + direction); Pair(4, Pair(4) + direction); Pair(0, Pair(0) - 1); } while (Pair(0) != 0);
                    }
                    else throw new Exception("Unsupported ED instruction: " + ed.ToString("X2"));
                    break;
                default: throw new Exception("Unsupported opcode " + op.ToString("X2") + " at " + (pc - 1));
            }
        }
        throw new Exception("Loader did not reach " + target);
    }
    internal static void Check(byte[] package, byte[] expanded, byte[] bitmap, bool restore)
    {
        LoaderMachine cpu = new LoaderMachine();
        byte[] original = (byte[])cpu.r.Clone(), alternate = (byte[])cpu.alt.Clone();
        cpu.Word(cpu.sp, 0x1234);
        Array.Copy(package, 0, cpu.memory, 32768, package.Length);
        cpu.RunTo(33280);
        if (!cpu.memory.Skip(33280).Take(expanded.Length).SequenceEqual(expanded)) throw new Exception("Z80 decoder produced different bytes.");
        cpu.RunTo(33291); // Three LD instructions and LDIR copy the raw bitmap.
        if (!cpu.memory.Skip(16384).Take(6144).SequenceEqual(bitmap)) throw new Exception("Z80 bitmap copy differs.");
        if (restore)
        {
            int entry = -1;
            for (int i = 0; i < expanded.Length - 5; i++)
                if (expanded[i] == 0xf3 && expanded[i + 1] == 0xed && expanded[i + 2] == 0x7b && expanded[i + 3] == 0xfe && expanded[i + 4] == 0x81) entry = 33280 + i;
            if (entry < 0) throw new Exception("Missing restorebasic entry.");
            cpu.pc = entry;
            cpu.RunTo(0x1234);
            if (!cpu.r.SequenceEqual(original) || !cpu.alt.SequenceEqual(alternate) || cpu.flags != 0xa5 || cpu.altFlags != 0x5a || cpu.ix != 0x1357 || cpu.iy != 0x2468 || cpu.sp != 32002)
                throw new Exception("BASIC return did not restore registers/SP.");
        }
    }
}
