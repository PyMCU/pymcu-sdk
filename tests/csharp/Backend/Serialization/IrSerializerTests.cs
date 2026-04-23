// SPDX-License-Identifier: MIT
// PyMCU Backend SDK — Unit tests for IrSerializer (JSON round-trip).

using FluentAssertions;
using PyMCU.Backend.Serialization;
using PyMCU.IR;
using Xunit;

namespace PyMCU.Backend.SDK.Tests.Backend.Serialization;

public class IrSerializerTests : IDisposable
{
    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), $"irser_{Guid.NewGuid():N}");

    public IrSerializerTests() => Directory.CreateDirectory(_tmpDir);

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
            Directory.Delete(_tmpDir, recursive: true);
    }

    private string TempFile(string name = "test.mir") => Path.Combine(_tmpDir, name);

    private static ProgramIR RoundTrip(ProgramIR prog)
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.mir");
        try
        {
            IrSerializer.Serialize(prog, tmp);
            return IrSerializer.Deserialize(tmp);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    // ── Empty program round-trip ───────────────────────────────────────────

    [Fact]
    public void Serialize_EmptyProgram_RoundTrips()
    {
        var prog = new ProgramIR();
        var rt = RoundTrip(prog);

        rt.Globals.Should().BeEmpty();
        rt.Functions.Should().BeEmpty();
        rt.ExternSymbols.Should().BeEmpty();
    }

    // ── Globals ────────────────────────────────────────────────────────────

    [Fact]
    public void Serialize_Globals_RoundTrip()
    {
        var prog = new ProgramIR();
        prog.Globals.Add(new Variable("led", DataType.UINT8));
        prog.Globals.Add(new Variable("counter", DataType.UINT16));

        var rt = RoundTrip(prog);

        rt.Globals.Should().HaveCount(2);
        rt.Globals[0].Name.Should().Be("led");
        rt.Globals[0].Type.Should().Be(DataType.UINT8);
        rt.Globals[1].Name.Should().Be("counter");
        rt.Globals[1].Type.Should().Be(DataType.UINT16);
    }

    // ── ExternSymbols ─────────────────────────────────────────────────────

    [Fact]
    public void Serialize_ExternSymbols_RoundTrip()
    {
        var prog = new ProgramIR();
        prog.ExternSymbols.AddRange(["uart_write", "delay_ms"]);

        var rt = RoundTrip(prog);

        rt.ExternSymbols.Should().BeEquivalentTo(["uart_write", "delay_ms"]);
    }

    // ── Function metadata ─────────────────────────────────────────────────

    [Fact]
    public void Serialize_FunctionMetadata_RoundTrips()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "isr_tim0",
            ReturnType = DataType.VOID,
            IsInterrupt = true,
            IsInline = false,
            InterruptVector = 2,
            Params = ["arg1"]
        });

        var rt = RoundTrip(prog);
        var f = rt.Functions[0];

        f.Name.Should().Be("isr_tim0");
        f.ReturnType.Should().Be(DataType.VOID);
        f.IsInterrupt.Should().BeTrue();
        f.InterruptVector.Should().Be(2);
        f.Params.Should().ContainSingle("arg1");
    }

    // ── Instruction type round-trips ───────────────────────────────────────

    [Fact]
    public void Serialize_ReturnWithConstant_RoundTrips()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body = [new Return(new Constant(42))]
        });

        var rt = RoundTrip(prog);
        var ret = rt.Functions[0].Body.OfType<Return>().First();

        ret.Value.Should().BeOfType<Constant>().Which.Value.Should().Be(42);
    }

    [Fact]
    public void Serialize_CopyInstruction_RoundTrips()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body = [new Copy(new Constant(1), new Variable("x", DataType.UINT8))]
        });

        var rt = RoundTrip(prog);
        var copy = rt.Functions[0].Body.OfType<Copy>().First();

        copy.Src.Should().BeOfType<Constant>().Which.Value.Should().Be(1);
        copy.Dst.Should().BeOfType<Variable>().Which.Name.Should().Be("x");
    }

    [Fact]
    public void Serialize_BinaryInstruction_RoundTrips()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body = [new Binary(BinaryOp.Add, new Variable("a"), new Variable("b"), new Temporary("t1"))]
        });

        var rt = RoundTrip(prog);
        var bin = rt.Functions[0].Body.OfType<Binary>().First();

        bin.Op.Should().Be(BinaryOp.Add);
        bin.Src1.Should().BeOfType<Variable>().Which.Name.Should().Be("a");
        bin.Src2.Should().BeOfType<Variable>().Which.Name.Should().Be("b");
        bin.Dst.Should().BeOfType<Temporary>().Which.Name.Should().Be("t1");
    }

    [Fact]
    public void Serialize_UnaryInstruction_RoundTrips()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body = [new Unary(UnaryOp.Neg, new Constant(5), new Temporary("t1"))]
        });

        var rt = RoundTrip(prog);
        var u = rt.Functions[0].Body.OfType<Unary>().First();

        u.Op.Should().Be(UnaryOp.Neg);
        u.Src.Should().BeOfType<Constant>().Which.Value.Should().Be(5);
    }

    [Fact]
    public void Serialize_BitInstructions_RoundTrip()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body =
            [
                new BitSet(new Variable("port"), 3),
                new BitClear(new Variable("port"), 5),
                new BitCheck(new Variable("port"), 2, new Temporary("t1")),
                new BitWrite(new Variable("port"), 4, new Variable("val")),
                new Return(new NoneVal())
            ]
        });

        var rt = RoundTrip(prog);
        var body = rt.Functions[0].Body;

        body.OfType<BitSet>().First().Bit.Should().Be(3);
        body.OfType<BitClear>().First().Bit.Should().Be(5);
        body.OfType<BitCheck>().First().Bit.Should().Be(2);
        body.OfType<BitWrite>().First().Bit.Should().Be(4);
    }

    [Fact]
    public void Serialize_JumpInstructions_RoundTrip()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body =
            [
                new JumpIfZero(new Variable("cond"), "end"),
                new Jump("end"),
                new Label("end"),
                new Return(new NoneVal())
            ]
        });

        var rt = RoundTrip(prog);
        var body = rt.Functions[0].Body;

        body.OfType<JumpIfZero>().First().Target.Should().Be("end");
        body.OfType<Jump>().First().Target.Should().Be("end");
        body.OfType<Label>().First().Name.Should().Be("end");
    }

    [Fact]
    public void Serialize_CallInstruction_RoundTrips()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body = [new Call("helper", [new Variable("a"), new Constant(1)], new Temporary("ret"))]
        });

        var rt = RoundTrip(prog);
        var call = rt.Functions[0].Body.OfType<Call>().First();

        call.FunctionName.Should().Be("helper");
        call.Args.Should().HaveCount(2);
        call.Dst.Should().BeOfType<Temporary>().Which.Name.Should().Be("ret");
    }

    [Fact]
    public void Serialize_ArrayLoadAndStore_RoundTrip()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body =
            [
                new ArrayLoad("buf", new Variable("i"), new Temporary("t1"), DataType.UINT8, 8),
                new ArrayStore("buf", new Variable("j"), new Variable("v"), DataType.UINT8, 8),
                new Return(new NoneVal())
            ]
        });

        var rt = RoundTrip(prog);
        var body = rt.Functions[0].Body;

        var al = body.OfType<ArrayLoad>().First();
        al.ArrayName.Should().Be("buf");
        al.Count.Should().Be(8);
        al.ElemType.Should().Be(DataType.UINT8);

        var ast = body.OfType<ArrayStore>().First();
        ast.ArrayName.Should().Be("buf");
    }

    [Fact]
    public void Serialize_FlashData_RoundTrips()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body = [new FlashData("lut", [0x00, 0x01, 0xFF]), new Return(new NoneVal())]
        });

        var rt = RoundTrip(prog);
        var fd = rt.Functions[0].Body.OfType<FlashData>().First();

        fd.Name.Should().Be("lut");
        fd.Bytes.Should().BeEquivalentTo([0x00, 0x01, 0xFF]);
    }

    [Fact]
    public void Serialize_MemoryAddress_RoundTrips()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body = [new Copy(new MemoryAddress(0x2000, DataType.UINT8), new Variable("x"))]
        });

        var rt = RoundTrip(prog);
        var copy = rt.Functions[0].Body.OfType<Copy>().First();

        copy.Src.Should().BeOfType<MemoryAddress>()
            .Which.Address.Should().Be(0x2000);
    }

    [Fact]
    public void Serialize_FloatConstant_RoundTrips()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body = [new Return(new FloatConstant(3.14))]
        });

        var rt = RoundTrip(prog);
        var ret = rt.Functions[0].Body.OfType<Return>().First();

        ret.Value.Should().BeOfType<FloatConstant>().Which.Value.Should().BeApproximately(3.14, 1e-9);
    }

    [Fact]
    public void Serialize_InlineAsm_WithOperands_RoundTrips()
    {
        var prog = new ProgramIR();
        prog.Functions.Add(new Function
        {
            Name = "main",
            Body = [new InlineAsm("clr %0", [new Variable("x")])]
        });

        var rt = RoundTrip(prog);
        var asm = rt.Functions[0].Body.OfType<InlineAsm>().First();

        asm.Code.Should().Be("clr %0");
        asm.Operands.Should().HaveCount(1);
    }

    // ── File not found ─────────────────────────────────────────────────────

    [Fact]
    public void Deserialize_MissingFile_Throws()
    {
        var act = () => IrSerializer.Deserialize(Path.Combine(_tmpDir, "nonexistent.mir"));
        act.Should().Throw<Exception>();
    }

    // ── Produces a file on disk ────────────────────────────────────────────

    [Fact]
    public void Serialize_CreatesFile()
    {
        var prog = new ProgramIR();
        var path = TempFile("out.mir");

        IrSerializer.Serialize(prog, path);

        File.Exists(path).Should().BeTrue();
        new FileInfo(path).Length.Should().BeGreaterThan(0);
    }
}
