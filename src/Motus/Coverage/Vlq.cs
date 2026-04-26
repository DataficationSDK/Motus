using System;
using System.Collections.Generic;

namespace Motus;

/// <summary>
/// Base64 VLQ encoding used by Source Map v3 <c>mappings</c> fields.
/// Each value is encoded as a sequence of base64 digits where the lowest bit of each
/// decoded sextet is a continuation flag and the lowest bit of the first sextet is
/// the sign of the resulting integer.
/// </summary>
internal static class Vlq
{
    private const string Base64Alphabet =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

    private static readonly sbyte[] DecodeTable = BuildDecodeTable();

    private static sbyte[] BuildDecodeTable()
    {
        var table = new sbyte[128];
        for (int i = 0; i < table.Length; i++) table[i] = -1;
        for (int i = 0; i < Base64Alphabet.Length; i++) table[Base64Alphabet[i]] = (sbyte)i;
        return table;
    }

    /// <summary>
    /// Decode a single VLQ-encoded segment (e.g. <c>"AAgBC"</c>) into the variable-length
    /// list of integers. A v3 source-map segment contains 1, 4, or 5 fields.
    /// </summary>
    /// <exception cref="FormatException">If the input contains a non-base64 character or terminates mid-value.</exception>
    public static IReadOnlyList<int> Decode(string segment)
    {
        var result = new List<int>(5);
        int value = 0;
        int shift = 0;
        bool inValue = false;

        for (int i = 0; i < segment.Length; i++)
        {
            char ch = segment[i];
            if (ch >= DecodeTable.Length || DecodeTable[ch] < 0)
                throw new FormatException($"Invalid base64 VLQ character '{ch}' at index {i}.");

            int digit = DecodeTable[ch];
            int chunk = digit & 0x1F;
            bool hasMore = (digit & 0x20) != 0;

            value |= chunk << shift;
            shift += 5;
            inValue = true;

            if (!hasMore)
            {
                bool negative = (value & 1) != 0;
                int magnitude = value >> 1;
                result.Add(negative ? -magnitude : magnitude);
                value = 0;
                shift = 0;
                inValue = false;
            }
        }

        if (inValue)
            throw new FormatException("VLQ segment ends with a continuation digit (incomplete value).");

        return result;
    }
}
