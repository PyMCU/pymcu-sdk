// SPDX-License-Identifier: MIT
// PyMCU Backend SDK — Unit tests for DynamicStackAllocator.

using FluentAssertions;
using PyMCU.Backend.Analysis;
using PyMCU.IR;
using Xunit;

namespace PyMCU.Backend.SDK.Tests.Backend.Analysis;

public class DynamicStackAllocatorTests
{
    private static Function MakeFunc(string name, IList<Instruction> body, params string[] parms)
    {
        return new Function
        {
            Name = name,
            Params = [.. parms],
            Body = [.. body]
        };
    }

    // ── Word size and reserved-top defaults ───────────────────────────────

    [Fact]
    public void Allocate_EmptyFunction_TotalSizeIsAlignedReserved()
    {
        // No locals, no params: totalSize = reservedTop rounded up to 16.
        // reservedTop=8 → -currentOffset starts at 8, totalSize=8 → rounds up to 16.
        var alloc = new DynamicStackAllocator(wordSize: 4, reservedTop: 8);
        var func = MakeFunc("main", [new Return(new NoneVal())]);
        var (offsets, total) = alloc.Allocate(func);

        offsets.Should().BeEmpty();
        total.Should().Be(16); // 8 rounded up to next multiple of 16
    }

    // ── Single variable allocation ─────────────────────────────────────────

    [Fact]
    public void Allocate_OneVariable_HasNegativeOffset()
    {
        var alloc = new DynamicStackAllocator(wordSize: 4, reservedTop: 8);
        var func = MakeFunc("main", [
            new Copy(new Constant(1), new Variable("x")),
            new Return(new Variable("x"))
        ]);
        var (offsets, total) = alloc.Allocate(func);

        offsets.Should().ContainKey("x");
        offsets["x"].Should().BeNegative(); // frame grows downwards
        total.Should().BeGreaterThan(0);
    }

    // ── Parameters come first ─────────────────────────────────────────────

    [Fact]
    public void Allocate_Params_AllocatedBeforeBodyLocals()
    {
        var alloc = new DynamicStackAllocator(wordSize: 4, reservedTop: 8);
        var func = MakeFunc("add", [
            new Binary(BinaryOp.Add, new Variable("a"), new Variable("b"), new Variable("result")),
            new Return(new Variable("result"))
        ], "a", "b");

        var (offsets, _) = alloc.Allocate(func);

        offsets.Should().ContainKeys("a", "b", "result");
        // Params a and b are allocated first → more-negative offsets (DynamicStackAllocator
        // grows downward, so first-allocated = highest address = least-negative offset).
        // Params come before body locals, so params have a HIGHER (less-negative) offset.
        offsets["result"].Should().BeLessThan(offsets["a"]);
        offsets["result"].Should().BeLessThan(offsets["b"]);
    }

    // ── Alignment: total size is always a multiple of 16 ─────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void Allocate_TotalSize_IsAlwaysMultipleOf16(int numVars)
    {
        var alloc = new DynamicStackAllocator(wordSize: 4, reservedTop: 0);
        var body = Enumerable.Range(0, numVars)
            .Select(i => (Instruction)new Copy(new Constant(i), new Variable($"v{i}")))
            .Append(new Return(new NoneVal()))
            .ToList();

        var func = MakeFunc("main", body);
        var (_, total) = alloc.Allocate(func);

        (total % 16).Should().Be(0);
    }

    // ── Each variable gets a unique offset ────────────────────────────────

    [Fact]
    public void Allocate_MultipleVars_AllGetDistinctOffsets()
    {
        var alloc = new DynamicStackAllocator(wordSize: 4, reservedTop: 0);
        var func = MakeFunc("main", [
            new Copy(new Constant(1), new Variable("a")),
            new Copy(new Constant(2), new Variable("b")),
            new Copy(new Constant(3), new Variable("c")),
            new Return(new NoneVal())
        ]);

        var (offsets, _) = alloc.Allocate(func);

        var vals = new[] { offsets["a"], offsets["b"], offsets["c"] };
        vals.Should().OnlyHaveUniqueItems();
    }

    // ── Same variable in multiple instructions — allocated once ───────────

    [Fact]
    public void Allocate_SameVarReferencedMultipleTimes_AllocatedOnce()
    {
        var alloc = new DynamicStackAllocator(wordSize: 4, reservedTop: 0);
        var func = MakeFunc("main", [
            new Copy(new Constant(1), new Variable("x")),
            new Copy(new Variable("x"), new Variable("y")),
            new Return(new Variable("x"))
        ]);

        var (offsets, _) = alloc.Allocate(func);

        offsets.Should().ContainKey("x");
        offsets.Keys.Count(k => k == "x").Should().Be(1); // only one entry for x
    }

    // ── Temporaries are also allocated ────────────────────────────────────

    [Fact]
    public void Allocate_Temporaries_AreIncluded()
    {
        var alloc = new DynamicStackAllocator(wordSize: 4, reservedTop: 0);
        var func = MakeFunc("main", [
            new Binary(BinaryOp.Add, new Variable("a"), new Variable("b"), new Temporary("t1")),
            new Return(new Temporary("t1"))
        ]);

        var (offsets, _) = alloc.Allocate(func);

        offsets.Should().ContainKey("t1");
    }

    // ── Bit instructions ────────────────────────────────────────────────────

    [Fact]
    public void Allocate_BitInstructions_RegisterVars()
    {
        var alloc = new DynamicStackAllocator(wordSize: 4, reservedTop: 0);
        var func = MakeFunc("main", [
            new BitSet(new Variable("port"), 3),
            new BitClear(new Variable("port"), 5),
            new BitCheck(new Variable("port"), 2, new Temporary("t1")),
            new BitWrite(new Variable("port"), 4, new Variable("val")),
            new Return(new NoneVal())
        ]);

        var (offsets, _) = alloc.Allocate(func);

        offsets.Should().ContainKeys("port", "t1", "val");
    }

    // ── Custom word size propagates correctly ─────────────────────────────

    [Fact]
    public void Allocate_CustomWordSize_OffsetsAreSeparatedByWordSize()
    {
        var alloc = new DynamicStackAllocator(wordSize: 2, reservedTop: 0);
        var func = MakeFunc("main", [
            new Copy(new Constant(1), new Variable("a")),
            new Copy(new Constant(2), new Variable("b")),
            new Return(new NoneVal())
        ]);

        var (offsets, _) = alloc.Allocate(func);

        Math.Abs(offsets["a"] - offsets["b"]).Should().Be(2);
    }
}
