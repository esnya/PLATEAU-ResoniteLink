using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PlateauResoniteLink.Application.Importing;

internal static class StableVariantSelector
{
    public static int SelectBucket(string variantSelectionKey, int bucketCount)
    {
        ArgumentNullException.ThrowIfNull(variantSelectionKey);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bucketCount);

        int keyByteCount = Encoding.UTF8.GetByteCount(variantSelectionKey);
        byte[]? rentedKeyBytes = null;
        Span<byte> keyBytes = keyByteCount <= 512
            ? stackalloc byte[keyByteCount]
            : rentedKeyBytes = ArrayPool<byte>.Shared.Rent(keyByteCount);
        keyBytes = keyBytes[..keyByteCount];

        try
        {
            Encoding.UTF8.GetBytes(variantSelectionKey, keyBytes);
            Span<byte> hashBytes = stackalloc byte[SHA256.HashSizeInBytes];
            if (!SHA256.TryHashData(keyBytes, hashBytes, out int hashBytesWritten)
                || hashBytesWritten != SHA256.HashSizeInBytes)
            {
                throw new CryptographicException("SHA-256 hash generation failed.");
            }

            int hashCode = BinaryPrimitives.ReadInt32LittleEndian(hashBytes) & int.MaxValue;
            return hashCode % bucketCount;
        }
        finally
        {
            if (rentedKeyBytes is not null)
            {
                ArrayPool<byte>.Shared.Return(rentedKeyBytes, clearArray: true);
            }
        }
    }
}
