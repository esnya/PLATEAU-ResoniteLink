using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PlateauResoniteLink.Application.Importing;

internal static class StableVariantSelector
{
    public static bool IsWeightedAlternate(string variantSelectionKey, string salt, int weight)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(salt);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(weight);
        return SelectBucket($"{variantSelectionKey}:{salt}", weight) == 0;
    }

    public static int SelectBucket(string variantSelectionKey, int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(variantSelectionKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketCount);

        byte[] keyBytes = Encoding.UTF8.GetBytes(variantSelectionKey);
        byte[] hashBytes = SHA256.HashData(keyBytes);
        int hashCode = BinaryPrimitives.ReadInt32LittleEndian(hashBytes) & int.MaxValue;
        return hashCode % bucketCount;
    }
}
