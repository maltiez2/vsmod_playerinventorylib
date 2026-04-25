using System;
using System.Text;

namespace PlayerInventoryLib;

/// <summary>
/// Efficiently appends/extracts a string slot ID to/from a Packet_ItemStack's
/// Attributes byte array by appending raw bytes after the serialized TreeAttribute,
/// avoiding full deserialization/reserialization.
///
/// Layout:
///   [original TreeAttribute bytes][UTF-8 string bytes][string byte length as int32 LE][magic marker 4 bytes]
///
/// The game's TreeAttribute.FromBytes reads only its own structure and stops,
/// so appended bytes are invisible to vanilla deserialization.
/// </summary>
public static class SlotIdByteEncoder
{
    // "SLID" in ASCII
    private static readonly byte[] MagicMarker = { 0x53, 0x4C, 0x49, 0x44 };
    private const int MarkerSize = 4;
    private const int LengthSize = 4;
    private const int TrailerMinSize = MarkerSize + LengthSize; // 8 bytes minimum (empty string)

    /// <summary>
    /// Appends the string slot ID to the end of the attributes byte array.
    /// If attributes is null or empty, creates a minimal array with just the slot ID.
    /// </summary>
    public static byte[] Append(byte[] attributes, string slotId)
    {
        byte[] original = attributes ?? Array.Empty<byte>();
        byte[] stringBytes = Encoding.UTF8.GetBytes(slotId);
        int stringLen = stringBytes.Length;

        // [original][stringBytes][stringLen as int32 LE][magic]
        byte[] result = new byte[original.Length + stringLen + LengthSize + MarkerSize];

        Buffer.BlockCopy(original, 0, result, 0, original.Length);
        Buffer.BlockCopy(stringBytes, 0, result, original.Length, stringLen);

        int offset = original.Length + stringLen;
        result[offset] = (byte)(stringLen);
        result[offset + 1] = (byte)(stringLen >> 8);
        result[offset + 2] = (byte)(stringLen >> 16);
        result[offset + 3] = (byte)(stringLen >> 24);

        Buffer.BlockCopy(MagicMarker, 0, result, offset + LengthSize, MarkerSize);

        return result;
    }

    /// <summary>
    /// Checks if the byte array has an appended string slot ID.
    /// </summary>
    public static bool HasSlotId(byte[] attributes)
    {
        if (attributes == null || attributes.Length < TrailerMinSize) return false;

        int len = attributes.Length;
        return attributes[len - 4] == MagicMarker[0]
            && attributes[len - 3] == MagicMarker[1]
            && attributes[len - 2] == MagicMarker[2]
            && attributes[len - 1] == MagicMarker[3];
    }

    /// <summary>
    /// Extracts the string slot ID without modifying the array.
    /// Returns null if no marker is found.
    /// </summary>
    public static string? Extract(byte[] attributes)
    {
        if (!HasSlotId(attributes)) return null;

        int len = attributes.Length;

        int stringLen = attributes[len - 8]
                     | (attributes[len - 7] << 8)
                     | (attributes[len - 6] << 16)
                     | (attributes[len - 5] << 24);

        if (stringLen < 0 || stringLen > len - TrailerMinSize) return null;

        int stringStart = len - TrailerMinSize - stringLen;
        if (stringStart < 0) return null;

        return Encoding.UTF8.GetString(attributes, stringStart, stringLen);
    }

    /// <summary>
    /// Returns the attributes byte array with the appended slot ID removed.
    /// Returns the original array if no marker is found.
    /// </summary>
    public static byte[] Strip(byte[] attributes)
    {
        if (!HasSlotId(attributes)) return attributes;

        int len = attributes.Length;

        int stringLen = attributes[len - 8]
                     | (attributes[len - 7] << 8)
                     | (attributes[len - 6] << 16)
                     | (attributes[len - 5] << 24);

        if (stringLen < 0 || stringLen > len - TrailerMinSize) return attributes;

        int originalLen = len - TrailerMinSize - stringLen;
        if (originalLen < 0) return attributes;

        byte[] result = new byte[originalLen];
        Buffer.BlockCopy(attributes, 0, result, 0, originalLen);
        return result;
    }
}