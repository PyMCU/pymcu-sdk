// SPDX-License-Identifier: MIT
// PyMCU Backend SDK — Unit tests for IR value and instruction record types.

using FluentAssertions;
using PyMCU.IR;
using Xunit;

namespace PyMCU.Backend.SDK.Tests.IR;

/// <summary>
/// Tests that Val subtypes and Instruction records have correct structural equality
/// (record semantics), default values, and can be constructed without error.
/// </summary>
public class ValRecordTests
{
    [Fact]
    public void Constant_Equality_ById_Value()
    {
        new Constant(42).Should().Be(new Constant(42));
        new Constant(1).Should().NotBe(new Constant(2));
    }

    [Fact]
    public void FloatConstant_Equality()
    {
        new FloatConstant(3.14).Should().Be(new FloatConstant(3.14));
        new FloatConstant(1.0).Should().NotBe(new FloatConstant(2.0));
    }

    [Fact]
    public void Variable_DefaultType_IsUInt8()
    {
        var v = new Variable("x");
        v.Type.Should().Be(DataType.UINT8);
    }

    [Fact]
    public void Variable_Equality_ConsidersNameAndType()
    {
        new Variable("x", DataType.UINT8).Should().Be(new Variable("x", DataType.UINT8));
        new Variable("x", DataType.UINT8).Should().NotBe(new Variable("x", DataType.UINT16));
        new Variable("x").Should().NotBe(new Variable("y"));
    }

    [Fact]
    public void Temporary_DefaultType_IsUInt8()
    {
        var t = new Temporary("t1");
        t.Type.Should().Be(DataType.UINT8);
    }

    [Fact]
    public void MemoryAddress_Equality()
    {
        new MemoryAddress(0x20).Should().Be(new MemoryAddress(0x20));
        new MemoryAddress(0x20, DataType.UINT16).Should().NotBe(new MemoryAddress(0x20, DataType.UINT8));
    }

    [Fact]
    public void NoneVal_Equality()
    {
        new NoneVal().Should().Be(new NoneVal());
    }

    [Fact]
    public void DifferentValTypes_AreNotEqual()
    {
        ((Val)new Constant(1)).Should().NotBe(new Variable("x"));
        ((Val)new NoneVal()).Should().NotBe(new Constant(0));
    }
}

public class InstructionRecordTests
{
    private static Variable V(string n, DataType t = DataType.UINT8) => new(n, t);
    private static Temporary T(string n, DataType t = DataType.UINT8) => new(n, t);
    private static Constant C(int v) => new(v);

    [Fact]
    public void Return_Equality()
    {
        new Return(C(0)).Should().Be(new Return(C(0)));
        new Return(C(0)).Should().NotBe(new Return(C(1)));
    }

    [Fact]
    public void Copy_Equality()
    {
        new Copy(C(1), V("x")).Should().Be(new Copy(C(1), V("x")));
        new Copy(C(1), V("x")).Should().NotBe(new Copy(C(2), V("x")));
    }

    [Fact]
    public void Unary_Equality()
    {
        new Unary(UnaryOp.Neg, C(5), T("t1"))
            .Should().Be(new Unary(UnaryOp.Neg, C(5), T("t1")));
        new Unary(UnaryOp.Neg, C(5), T("t1"))
            .Should().NotBe(new Unary(UnaryOp.Not, C(5), T("t1")));
    }

    [Fact]
    public void Binary_Equality()
    {
        new Binary(BinaryOp.Add, V("a"), V("b"), T("t"))
            .Should().Be(new Binary(BinaryOp.Add, V("a"), V("b"), T("t")));
        new Binary(BinaryOp.Add, V("a"), V("b"), T("t"))
            .Should().NotBe(new Binary(BinaryOp.Sub, V("a"), V("b"), T("t")));
    }

    [Fact]
    public void Jump_StoresTarget()
    {
        var j = new Jump("loop_start");
        j.Target.Should().Be("loop_start");
    }

    [Fact]
    public void JumpIfZero_StoresConditionAndTarget()
    {
        var jz = new JumpIfZero(V("flag"), "end");
        jz.Condition.Should().Be(V("flag"));
        jz.Target.Should().Be("end");
    }

    [Fact]
    public void Label_StoresName()
    {
        new Label("loop").Name.Should().Be("loop");
    }

    [Fact]
    public void Call_StoresArgsAndDst()
    {
        var call = new Call("foo", [V("a"), V("b")], T("ret"));
        call.FunctionName.Should().Be("foo");
        call.Args.Should().HaveCount(2);
        call.Dst.Should().Be(T("ret"));
    }

    [Fact]
    public void BitSet_StoresBit()
    {
        var bs = new BitSet(V("port"), 3);
        bs.Bit.Should().Be(3);
        bs.Target.Should().Be(V("port"));
    }

    [Fact]
    public void BitClear_StoresBit()
    {
        var bc = new BitClear(V("port"), 5);
        bc.Bit.Should().Be(5);
    }

    [Fact]
    public void BitCheck_StoresSourceBitAndDst()
    {
        var bck = new BitCheck(V("port"), 2, T("t1"));
        bck.Source.Should().Be(V("port"));
        bck.Bit.Should().Be(2);
        bck.Dst.Should().Be(T("t1"));
    }

    [Fact]
    public void BitWrite_StoresAllFields()
    {
        var bw = new BitWrite(V("port"), 4, V("val"));
        bw.Target.Should().Be(V("port"));
        bw.Bit.Should().Be(4);
        bw.Src.Should().Be(V("val"));
    }

    [Fact]
    public void AugAssign_StoresOpTargetOperand()
    {
        var aug = new AugAssign(BinaryOp.Add, V("x"), C(1));
        aug.Op.Should().Be(BinaryOp.Add);
        aug.Target.Should().Be(V("x"));
        aug.Operand.Should().Be(C(1));
    }

    [Fact]
    public void InlineAsm_WithoutOperands_HasNullOperands()
    {
        var asm = new InlineAsm("nop");
        asm.Code.Should().Be("nop");
        asm.Operands.Should().BeNull();
    }

    [Fact]
    public void InlineAsm_WithOperands_StoresList()
    {
        var asm = new InlineAsm("mov %0, %1", [V("a"), V("b")]);
        asm.Operands.Should().HaveCount(2);
    }

    [Fact]
    public void DebugLine_StoresAllFields()
    {
        var dbg = new DebugLine(42, "x = 1", "main.py");
        dbg.Line.Should().Be(42);
        dbg.Text.Should().Be("x = 1");
        dbg.SourceFile.Should().Be("main.py");
    }

    [Fact]
    public void ArrayLoad_StoresAllFields()
    {
        var al = new ArrayLoad("arr", V("i"), T("t1"), DataType.UINT8, 16);
        al.ArrayName.Should().Be("arr");
        al.ElemType.Should().Be(DataType.UINT8);
        al.Count.Should().Be(16);
    }

    [Fact]
    public void ArrayStore_StoresAllFields()
    {
        var ast = new ArrayStore("arr", V("i"), V("val"), DataType.UINT8, 8);
        ast.ArrayName.Should().Be("arr");
        ast.ElemType.Should().Be(DataType.UINT8);
        ast.Count.Should().Be(8);
    }

    [Fact]
    public void ArrayLoadFlash_StoresAllFields()
    {
        var alf = new ArrayLoadFlash("lut", V("i"), T("t1"));
        alf.ArrayName.Should().Be("lut");
    }

    [Fact]
    public void FlashData_StoresNameAndBytes()
    {
        var fd = new FlashData("lut", [0x01, 0x02, 0x03]);
        fd.Name.Should().Be("lut");
        fd.Bytes.Should().BeEquivalentTo([1, 2, 3]);
    }

    [Fact]
    public void LoadIndirect_StoresSrcPtrAndDst()
    {
        var li = new LoadIndirect(V("ptr"), T("t1"));
        li.SrcPtr.Should().Be(V("ptr"));
        li.Dst.Should().Be(T("t1"));
    }

    [Fact]
    public void StoreIndirect_StoresSrcAndDstPtr()
    {
        var si = new StoreIndirect(V("val"), V("ptr"));
        si.Src.Should().Be(V("val"));
        si.DstPtr.Should().Be(V("ptr"));
    }
}

public class ProgramIRTests
{
    [Fact]
    public void ProgramIR_Default_HasEmptyCollections()
    {
        var prog = new ProgramIR();
        prog.Globals.Should().BeEmpty();
        prog.Functions.Should().BeEmpty();
        prog.ExternSymbols.Should().BeEmpty();
    }

    [Fact]
    public void Function_Default_HasEmptyCollections()
    {
        var func = new Function();
        func.Name.Should().BeEmpty();
        func.Params.Should().BeEmpty();
        func.Body.Should().BeEmpty();
        func.ReturnType.Should().Be(DataType.VOID);
        func.IsInline.Should().BeFalse();
        func.IsInterrupt.Should().BeFalse();
    }

    [Fact]
    public void ProgramIR_CanAddGlobalsAndFunctions()
    {
        var prog = new ProgramIR();
        prog.Globals.Add(new Variable("led", DataType.UINT8));
        prog.Functions.Add(new Function { Name = "main" });
        prog.ExternSymbols.Add("uart_write");

        prog.Globals.Should().HaveCount(1);
        prog.Functions.Should().HaveCount(1);
        prog.ExternSymbols.Should().ContainSingle("uart_write");
    }
}
