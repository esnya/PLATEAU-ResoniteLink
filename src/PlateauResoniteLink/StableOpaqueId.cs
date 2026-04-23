using System;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace PlateauResoniteLink;

internal static class StableOpaqueId
{
    public static string Create(string prefix, Action<Builder> build, int hexLength = 24)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
        ArgumentNullException.ThrowIfNull(build);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(hexLength);

        using Builder builder = new();
        build(builder);
        return string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"{prefix}-{builder.GetHex(hexLength)}");
    }

    internal sealed class Builder : IDisposable
    {
        private readonly IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        public void Add(string? value)
        {
            AddTag(1);
            if (value is null)
            {
                AddInt32(-1);
                return;
            }

            byte[] bytes = Encoding.UTF8.GetBytes(value);
            AddInt32(bytes.Length);
            hash.AppendData(bytes);
        }

        public void Add(int value)
        {
            AddTag(2);
            AddInt32(value);
        }

        public void Add(int? value)
        {
            AddTag(3);
            Add(value.HasValue);
            if (value.HasValue)
            {
                AddInt32(value.Value);
            }
        }

        public void Add(double value)
        {
            AddTag(4);
            Span<byte> bytes = stackalloc byte[sizeof(double)];
            BinaryPrimitives.WriteInt64LittleEndian(bytes, BitConverter.DoubleToInt64Bits(value));
            hash.AppendData(bytes);
        }

        public void AddRounded(double value, int decimals = 6)
        {
            double rounded = Math.Round(value, decimals, MidpointRounding.AwayFromZero);
            Add(rounded == 0.0 ? 0.0 : rounded);
        }

        public void Add(double? value)
        {
            AddTag(7);
            Add(value.HasValue);
            if (value.HasValue)
            {
                Add(value.Value);
            }
        }

        public void AddRounded(double? value, int decimals = 6)
        {
            AddTag(8);
            Add(value.HasValue);
            if (value.HasValue)
            {
                AddRounded(value.Value, decimals);
            }
        }

        public void Add(bool value)
        {
            AddTag(5);
            Span<byte> bytes = stackalloc byte[1];
            bytes[0] = value ? (byte)1 : (byte)0;
            hash.AppendData(bytes);
        }

        public void AddEnum<T>(T value)
            where T : struct, Enum
        {
            AddTag(6);
            Add(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
        }

        public string GetHex(int hexLength)
        {
            byte[] digest = hash.GetHashAndReset();
            int byteCount = Math.Min(digest.Length, (hexLength + 1) / 2);
            string hex = Convert.ToHexString(digest.AsSpan(0, byteCount)).ToLowerInvariant();
            return hex.Length > hexLength
                ? hex[..hexLength]
                : hex;
        }

        public void Dispose()
        {
            hash.Dispose();
        }

        private void AddTag(byte value)
        {
            Span<byte> bytes = stackalloc byte[1];
            bytes[0] = value;
            hash.AppendData(bytes);
        }

        private void AddInt32(int value)
        {
            Span<byte> bytes = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            hash.AppendData(bytes);
        }
    }
}
