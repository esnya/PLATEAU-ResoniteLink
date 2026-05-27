using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PlateauResoniteLink.Application.Importing;

internal static class StableVariantSelector
{
    public static int SelectBucket(string variantSelectionKey, int bucketCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(variantSelectionKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketCount);

        byte[] keyBytes = Encoding.UTF8.GetBytes(variantSelectionKey);
        byte[] hashBytes = SHA256.HashData(keyBytes);
        int hashCode = BinaryPrimitives.ReadInt32LittleEndian(hashBytes) & int.MaxValue;
        return hashCode % bucketCount;
    }
}
