// SPDX-License-Identifier: MIT
// PyMCU Backend SDK — Unit tests for StackAllocator.

using FluentAssertions;
using PyMCU.Backend.Analysis;
using PyMCU.IR;
using Xunit;

namespace PyMCU.Backend.SDK.Tests.Backend.Analysis;

public class StackAllocatorTests
{
    private static ProgramIR MakeProgram(
        IEnumerable<Variable>? globals = null,
        IEnumerable<Function>? funcs = null)
    {
        var prog = new ProgramIR();
        if (globals != null) prog.Globals.AddRange(globals);
        if (funcs != null) prog.Functions.AddRange(funcs);
        return prog;
    }

    private static Function MakeFunc(string name, IList<Instruction> body, bool isInterrupt = false, int vector = 0, IList<string>? parms = null)
    {
        return new Function
        {
            Name = name,
            Params = parms != null ? [.. parms] : [],
            Body = [.. body],
            IsInterrupt = isInterrupt,
            InterruptVector = vector
        };
    }

    // ── Global allocation ──────────────────────────────────────────────────

    [Fact]
    public void Allocate_NoFunctions_AllocatesGlobals()
    {
        var prog = MakeProgram(globals: [
            new Variable("a", DataType.UINT8),
            new Variable("b", DataType.UINT16)
        ]);

        var allocator = new StackAllocator();
        var (offsets, maxStack) = allocator.Allocate(prog);

        offsets["a"].Should().Be(0);
        offsets["b"].Should().Be(1);  // UINT8 occupies 1 byte
        maxStack.Should().Be(3);      // 1 + 2
    }

    // ── Single function — local variables ─────────────────────────────────

    [Fact]
    public void Allocate_SingleFunction_AssignsSequentialOffsets()
    {
        var prog = MakeProgram(funcs: [MakeFunc("main", [
            new Copy(new Constant(1), new Variable("x")),
            new Copy(new Constant(2), new Variable("y")),
            new Return(new Variable("x"))
        ])]);

        var allocator = new StackAllocator();
        var (offsets, maxStack) = allocator.Allocate(prog);

        offsets.Should().ContainKey("x");
        offsets.Should().ContainKey("y");
        // x and y are 1-byte locals starting at 0
        offsets["x"].Should().Be(0);
        offsets["y"].Should().Be(1);
        maxStack.Should().BeGreaterThanOrEqualTo(2);
    }

    // ── Parameter registration ─────────────────────────────────────────────

    [Fact]
    public void Allocate_FunctionParams_AreRegisteredAsLocals()
    {
        // "add" is called from main — params get allocated as part of the call graph.
        var prog = MakeProgram(funcs: [
            MakeFunc("main", [
                new Call("add", [new Constant(1), new Constant(2)], new Variable("out")),
                new Return(new Variable("out"))
            ]),
            MakeFunc("add", [
                new Binary(BinaryOp.Add, new Variable("a"), new Variable("b"), new Variable("result")),
                new Return(new Variable("result"))
            ], parms: ["a", "b"])
        ]);

        var allocator = new StackAllocator();
        var (offsets, _) = allocator.Allocate(prog);

        // params a and b should be in the offset table
        offsets.Should().ContainKey("a");
        offsets.Should().ContainKey("b");
    }

    // ── Globals vs locals: globals must not be re-allocated in functions ───

    [Fact]
    public void Allocate_GlobalNotReallocatedAsLocal()
    {
        var prog = MakeProgram(
            globals: [new Variable("led", DataType.UINT8)],
            funcs: [MakeFunc("main", [
                new Copy(new Constant(1), new Variable("led")),   // led is global
                new Copy(new Constant(0), new Variable("local")),
                new Return(new NoneVal())
            ])]);

        var allocator = new StackAllocator();
        var (offsets, maxStack) = allocator.Allocate(prog);

        // led is global at offset 0
        offsets["led"].Should().Be(0);
        // local starts right after the globals (offset 1)
        offsets["local"].Should().Be(1);
    }

    // ── Call graph: caller + callee stack frames are sequenced ─────────────

    [Fact]
    public void Allocate_CallerCallee_SequencesFrames()
    {
        var callee = MakeFunc("helper", [
            new Return(new Variable("ret"))
        ], parms: ["ret"]);

        var caller = MakeFunc("main", [
            new Call("helper", [new Constant(1)], new Variable("res")),
            new Return(new Variable("res"))
        ]);

        var prog = MakeProgram(funcs: [caller, callee]);
        var allocator = new StackAllocator();
        var (offsets, maxStack) = allocator.Allocate(prog);

        // caller's locals come first, then callee's locals start at a higher offset
        offsets.Should().ContainKey("res");
        offsets.Should().ContainKey("ret");
        offsets["ret"].Should().BeGreaterThan(offsets["res"]);
    }

    // ── Interrupt functions are also allocated ─────────────────────────────

    [Fact]
    public void Allocate_InterruptFunction_GetsOffsets()
    {
        var isr = MakeFunc("isr_tim0", [
            new Copy(new Constant(0), new Variable("tick")),
            new Return(new NoneVal())
        ], isInterrupt: true, vector: 1);

        var main = MakeFunc("main", [new Return(new NoneVal())]);

        var prog = MakeProgram(funcs: [main, isr]);
        var allocator = new StackAllocator();
        var (offsets, _) = allocator.Allocate(prog);

        offsets.Should().ContainKey("tick");
    }

    // ── VariableSizes is populated ────────────────────────────────────────

    [Fact]
    public void Allocate_VariableSizes_ReflectsDataTypes()
    {
        var prog = MakeProgram(funcs: [MakeFunc("main", [
            new Copy(new Constant(0), new Variable("b8",  DataType.UINT8)),
            new Copy(new Constant(0), new Variable("b16", DataType.UINT16)),
            new Copy(new Constant(0), new Variable("b32", DataType.UINT32)),
            new Return(new NoneVal())
        ])]);

        var allocator = new StackAllocator();
        allocator.Allocate(prog);

        allocator.VariableSizes["b8"].Should().Be(1);
        allocator.VariableSizes["b16"].Should().Be(2);
        allocator.VariableSizes["b32"].Should().Be(4);
    }

    // ── BitSet/BitClear/BitCheck/BitWrite variables are captured ──────────

    [Fact]
    public void Allocate_BitInstructions_RegisterVars()
    {
        var prog = MakeProgram(funcs: [MakeFunc("main", [
            new BitSet(new Variable("port"), 3),
            new BitClear(new Variable("port"), 5),
            new BitCheck(new Variable("port"), 2, new Temporary("t1")),
            new BitWrite(new Variable("port"), 4, new Variable("val")),
            new Return(new NoneVal())
        ])]);

        var allocator = new StackAllocator();
        var (offsets, _) = allocator.Allocate(prog);

        offsets.Should().ContainKey("port");
        offsets.Should().ContainKey("t1");
        offsets.Should().ContainKey("val");
    }

    // ── Array instructions register array name with correct total size ─────

    [Fact]
    public void Allocate_ArrayLoad_RegistersArrayWithTotalByteSize()
    {
        var prog = MakeProgram(funcs: [MakeFunc("main", [
            new ArrayLoad("buf", new Variable("i"), new Temporary("t1"), DataType.UINT8, 10),
            new Return(new NoneVal())
        ])]);

        var allocator = new StackAllocator();
        allocator.Allocate(prog);

        allocator.VariableSizes["buf"].Should().Be(10); // 10 × UINT8 (1 byte)
    }

    [Fact]
    public void Allocate_ArrayStore_RegistersArrayWithTotalByteSize()
    {
        var prog = MakeProgram(funcs: [MakeFunc("main", [
            new ArrayStore("buf", new Variable("i"), new Variable("v"), DataType.UINT16, 4),
            new Return(new NoneVal())
        ])]);

        var allocator = new StackAllocator();
        allocator.Allocate(prog);

        allocator.VariableSizes["buf"].Should().Be(8); // 4 × UINT16 (2 bytes)
    }
}
