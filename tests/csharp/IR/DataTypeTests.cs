// SPDX-License-Identifier: MIT
// PyMCU Backend SDK — Unit tests for DataType and DataTypeExtensions.

using FluentAssertions;
using PyMCU.IR;
using Xunit;

namespace PyMCU.Backend.SDK.Tests.IR;

public class DataTypeSizeOfTests
{
    [Theory]
    [InlineData(DataType.UINT8,  1)]
    [InlineData(DataType.INT8,   1)]
    [InlineData(DataType.UINT16, 2)]
    [InlineData(DataType.INT16,  2)]
    [InlineData(DataType.UINT32, 4)]
    [InlineData(DataType.INT32,  4)]
    [InlineData(DataType.FLOAT,  4)]
    [InlineData(DataType.VOID,   1)] // falls through to default
    [InlineData(DataType.UNKNOWN,1)] // falls through to default
    public void SizeOf_ReturnsExpectedBytes(DataType type, int expected)
    {
        type.SizeOf().Should().Be(expected);
    }
}

public class DataTypeIsSignedTests
{
    [Theory]
    [InlineData(DataType.INT8,   true)]
    [InlineData(DataType.INT16,  true)]
    [InlineData(DataType.INT32,  true)]
    [InlineData(DataType.UINT8,  false)]
    [InlineData(DataType.UINT16, false)]
    [InlineData(DataType.UINT32, false)]
    [InlineData(DataType.FLOAT,  false)]
    [InlineData(DataType.VOID,   false)]
    [InlineData(DataType.UNKNOWN,false)]
    public void IsSigned_ReturnsExpectedResult(DataType type, bool expected)
    {
        type.IsSigned().Should().Be(expected);
    }
}

public class DataTypeStringToDataTypeTests
{
    [Theory]
    [InlineData("",        DataType.UINT8)]  // empty → UINT8
    [InlineData("uint8",   DataType.UINT8)]
    [InlineData("int",     DataType.INT16)]
    [InlineData("int8",    DataType.INT8)]
    [InlineData("uint16",  DataType.UINT16)]
    [InlineData("int16",   DataType.INT16)]
    [InlineData("uint32",  DataType.UINT32)]
    [InlineData("int32",   DataType.INT32)]
    [InlineData("float",   DataType.FLOAT)]
    [InlineData("const",   DataType.UINT8)]
    [InlineData("void",    DataType.VOID)]
    [InlineData("None",    DataType.VOID)]
    [InlineData("ptr",     DataType.UINT16)]
    public void StringToDataType_KnownStrings(string input, DataType expected)
    {
        DataTypeExtensions.StringToDataType(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("const[uint8]",  DataType.UINT8)]
    [InlineData("const[uint16]", DataType.UINT16)]
    [InlineData("const[int8]",   DataType.INT8)]
    [InlineData("const[int32]",  DataType.INT32)]
    public void StringToDataType_ConstBracket_ExtractsInnerType(string input, DataType expected)
    {
        DataTypeExtensions.StringToDataType(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("ptr[uint8]",  DataType.UINT8)]
    [InlineData("ptr[uint16]", DataType.UINT16)]
    [InlineData("ptr[int32]",  DataType.INT32)]
    public void StringToDataType_PtrBracket_ExtractsInnerType(string input, DataType expected)
    {
        DataTypeExtensions.StringToDataType(input).Should().Be(expected);
    }

    [Fact]
    public void StringToDataType_PioRegister_ReturnsUInt16()
    {
        DataTypeExtensions.StringToDataType("SomePIORegister").Should().Be(DataType.UINT16);
    }

    [Fact]
    public void StringToDataType_Null_ReturnsUInt8()
    {
        // null is treated as empty/null by string.IsNullOrEmpty
        DataTypeExtensions.StringToDataType(null!).Should().Be(DataType.UINT8);
    }

    [Fact]
    public void StringToDataType_UnknownString_ReturnsUnknown()
    {
        DataTypeExtensions.StringToDataType("bogus_type").Should().Be(DataType.UNKNOWN);
    }
}

public class DataTypeGetPromotedTypeTests
{
    [Fact]
    public void GetPromotedType_SameType_ReturnsSame()
    {
        DataTypeExtensions.GetPromotedType(DataType.UINT8, DataType.UINT8).Should().Be(DataType.UINT8);
        DataTypeExtensions.GetPromotedType(DataType.INT16, DataType.INT16).Should().Be(DataType.INT16);
        DataTypeExtensions.GetPromotedType(DataType.UINT32, DataType.UINT32).Should().Be(DataType.UINT32);
    }

    [Fact]
    public void GetPromotedType_LargerType_WinsRegardlessOfSignedness()
    {
        // uint16 (2) vs int8 (1) → uint16 wins (larger size)
        DataTypeExtensions.GetPromotedType(DataType.UINT16, DataType.INT8).Should().Be(DataType.UINT16);
        DataTypeExtensions.GetPromotedType(DataType.INT8, DataType.UINT16).Should().Be(DataType.UINT16);
    }

    [Fact]
    public void GetPromotedType_SameSize_DifferentSignedness_PromotesToNextSigned()
    {
        // uint8 (unsigned 1-byte) vs int8 (signed 1-byte) → INT16
        DataTypeExtensions.GetPromotedType(DataType.UINT8, DataType.INT8).Should().Be(DataType.INT16);
        DataTypeExtensions.GetPromotedType(DataType.INT8, DataType.UINT8).Should().Be(DataType.INT16);
    }

    [Fact]
    public void GetPromotedType_SameSize_SameSignedness_ReturnsFirst()
    {
        // Both unsigned 1-byte: same size, same signedness → returns first (a)
        DataTypeExtensions.GetPromotedType(DataType.UINT8, DataType.UINT8).Should().Be(DataType.UINT8);
    }

    [Fact]
    public void GetPromotedType_Uint16_vs_Int16_PromotesToInt32()
    {
        // Same size (2 bytes), different signedness → INT32
        DataTypeExtensions.GetPromotedType(DataType.UINT16, DataType.INT16).Should().Be(DataType.INT32);
        DataTypeExtensions.GetPromotedType(DataType.INT16, DataType.UINT16).Should().Be(DataType.INT32);
    }

    [Fact]
    public void GetPromotedType_Int32_vs_Uint32_PromotesToInt32()
    {
        // Same size (4 bytes), different signedness → INT32 (capped at largest signed)
        DataTypeExtensions.GetPromotedType(DataType.INT32, DataType.UINT32).Should().Be(DataType.INT32);
    }

    [Fact]
    public void GetPromotedType_Float_vs_Int32_ReturnsFloat()
    {
        // float and int32 both 4 bytes but float is unsigned-classified → INT32 wins via signedness
        // float.IsSigned() is false, int32.IsSigned() is true, same size → INT32
        DataTypeExtensions.GetPromotedType(DataType.FLOAT, DataType.INT32).Should().Be(DataType.INT32);
    }

    [Fact]
    public void GetPromotedType_Uint32_vs_Int8_ReturnsUInt32()
    {
        // uint32 (4) > int8 (1) → UINT32
        DataTypeExtensions.GetPromotedType(DataType.UINT32, DataType.INT8).Should().Be(DataType.UINT32);
    }
}
