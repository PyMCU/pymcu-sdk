/*
 * -----------------------------------------------------------------------------
 * PyMCU Compiler (pymcuc)
 * Copyright (C) 2026 Ivan Montiel Cardona and the PyMCU Project Authors
 *
 * SPDX-License-Identifier: MIT
 *
 * -----------------------------------------------------------------------------
 * SAFETY WARNING / HIGH RISK ACTIVITIES:
 * THE SOFTWARE IS NOT DESIGNED, MANUFACTURED, OR INTENDED FOR USE IN HAZARDOUS
 * ENVIRONMENTS REQUIRING FAIL-SAFE PERFORMANCE, SUCH AS IN THE OPERATION OF
 * NUCLEAR FACILITIES, AIRCRAFT NAVIGATION OR COMMUNICATION SYSTEMS, AIR
 * TRAFFIC CONTROL, DIRECT LIFE SUPPORT MACHINES, OR WEAPONS SYSTEMS.
 * -----------------------------------------------------------------------------
 */

namespace PyMCU.IR;

public enum DataType
{
    UINT8,
    INT8,
    UINT16,
    INT16,
    UINT32,
    INT32,
    FLOAT, // Placeholder for future support
    VOID,
    UNKNOWN
}

public static class DataTypeExtensions
{
    /// Returns the byte count for a given DataType.
    public static int SizeOf(this DataType type)
    {
        return type switch
        {
            DataType.UINT8 or DataType.INT8 => 1,
            DataType.UINT16 or DataType.INT16 => 2,
            DataType.UINT32 or DataType.INT32 or DataType.FLOAT => 4,
            _ => 1
        };
    }

    /// Returns true if the DataType is a signed integer type.
    public static bool IsSigned(this DataType type)
    {
        return type switch
        {
            DataType.INT8 or DataType.INT16 or DataType.INT32 => true,
            _ => false
        };
    }

    /// Maps a Python type annotation string to an internal DataType enum.
    public static DataType StringToDataType(string typeStr)
    {
        if (string.IsNullOrEmpty(typeStr) || typeStr == "uint8")
            return DataType.UINT8;
        switch (typeStr)
        {
            case "int":
                return DataType.INT16;
            case "int8":
                return DataType.INT8;
            case "uint16":
                return DataType.UINT16;
            case "int16":
                return DataType.INT16;
            case "uint32":
                return DataType.UINT32;
            case "int32":
                return DataType.INT32;
            case "float":
                return DataType.FLOAT;
            case "const":
                return DataType.UINT8; // Compile-time only, never allocated
        }

        // Handle const[TYPE] — extract inner type (e.g., const[uint8] -> uint8)
        if (typeStr.StartsWith("const[") && typeStr.EndsWith("]"))
        {
            var inner = typeStr.Substring(6, typeStr.Length - 7);
            return StringToDataType(inner);
        }

        if (typeStr == "void" || typeStr == "None") return DataType.VOID;

        // For pointer/register types, extract the inner element type (e.g. ptr[uint8] -> UINT8)
        if (typeStr.StartsWith("ptr[") && typeStr.EndsWith("]"))
        {
            var inner = typeStr.Substring(4, typeStr.Length - 5);
            return StringToDataType(inner);
        }
        // Bare ptr (no inner type) or PIORegister — address-level default
        if (typeStr == "ptr" || typeStr.Contains("PIORegister"))
            return DataType.UINT16;

        return DataType.UNKNOWN;
    }

    /// Returns the promoted type when combining two operand types.
    public static DataType GetPromotedType(DataType a, DataType b)
    {
        if (a == b) return a;

        var sizeA = a.SizeOf();
        var sizeB = b.SizeOf();

        // Promote to the larger type
        if (sizeA > sizeB) return a;
        if (sizeB > sizeA) return b;

        // Same size, differing signedness — promote to signed variant of next size
        var aSigned = a.IsSigned();
        var bSigned = b.IsSigned();

        if (aSigned == bSigned) return a;
        return sizeA switch
        {
            1 => DataType.INT16,
            2 => DataType.INT32,
            _ => DataType.INT32
        };

        // Same size, same signedness — prefer 'a'
    }
}