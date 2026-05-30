using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PlateauResoniteLink.Application.Importing;

internal static class GeoTiffTagReader
{
    private const ushort ClassicTiffMagic = 42;
    private const ushort BigTiffMagic = 43;
    private const ushort TypeAscii = 2;
    private const ushort TypeShort = 3;
    private const ushort TypeLong = 4;
    private const ushort TypeDouble = 12;
    private const ushort PixelScaleTag = 33550;
    private const ushort ModelTiePointTag = 33922;
    private const ushort ModelTransformTag = 34264;
    private const ushort GeoKeyDirectoryTag = 34735;
    private const ushort GeoDoubleParamsTag = 34736;
    private const ushort GeoAsciiParamsTag = 34737;

    public static async Task<GeoTiffTagSnapshot?> TryReadAsync(string sourcePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
        {
            return null;
        }

        try
        {
            await using FileStream stream = new(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous);
            return await TryReadAsync(stream, cancellationToken);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    internal static async Task<GeoTiffTagSnapshot?> TryReadAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
        {
            return null;
        }

        byte[] classicHeader = new byte[8];
        if (!await TryReadExactAsync(stream, 0, classicHeader, cancellationToken))
        {
            return null;
        }

        bool littleEndian = classicHeader[0] == (byte)'I' && classicHeader[1] == (byte)'I';
        if (!littleEndian && !(classicHeader[0] == (byte)'M' && classicHeader[1] == (byte)'M'))
        {
            return null;
        }

        ushort magic = ReadUInt16(classicHeader, 2, littleEndian);
        return magic switch
        {
            ClassicTiffMagic => await TryReadClassicAsync(stream, littleEndian, ReadUInt32(classicHeader, 4, littleEndian), cancellationToken),
            BigTiffMagic => await TryReadBigTiffAsync(stream, littleEndian, cancellationToken),
            _ => null,
        };
    }

    internal static GeoTiffTagSnapshot? TryRead(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 8)
        {
            return null;
        }

        bool littleEndian = bytes[0] == (byte)'I' && bytes[1] == (byte)'I';
        if (!littleEndian && !(bytes[0] == (byte)'M' && bytes[1] == (byte)'M'))
        {
            return null;
        }

        ushort magic = ReadUInt16(bytes, 2, littleEndian);
        return magic switch
        {
            ClassicTiffMagic => TryReadClassic(bytes, littleEndian),
            BigTiffMagic => TryReadBigTiff(bytes, littleEndian),
            _ => null,
        };
    }

    private static GeoTiffTagSnapshot? TryReadClassic(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        uint ifdOffset = ReadUInt32(bytes, 4, littleEndian);
        if (ifdOffset >= bytes.Length || ifdOffset + 2 > bytes.Length)
        {
            return null;
        }

        ushort entryCount = ReadUInt16(bytes, checked((int)ifdOffset), littleEndian);
        int entriesOffset = checked((int)ifdOffset) + 2;
        Dictionary<ushort, object> values = [];
        for (int index = 0; index < entryCount; index++)
        {
            int entryOffset = entriesOffset + (index * 12);
            if (entryOffset + 12 > bytes.Length)
            {
                break;
            }

            TryReadClassicEntry(bytes, entryOffset, littleEndian, values);
        }

        return ToSnapshot(values);
    }

    private static GeoTiffTagSnapshot? TryReadBigTiff(ReadOnlySpan<byte> bytes, bool littleEndian)
    {
        if (bytes.Length < 16)
        {
            return null;
        }

        ushort offsetSize = ReadUInt16(bytes, 4, littleEndian);
        if (offsetSize != 8)
        {
            return null;
        }

        ulong ifdOffset = ReadUInt64(bytes, 8, littleEndian);
        if (ifdOffset >= (ulong)bytes.Length || ifdOffset + 8 > (ulong)bytes.Length)
        {
            return null;
        }

        ulong entryCount = ReadUInt64(bytes, checked((int)ifdOffset), littleEndian);
        int entriesOffset = checked((int)ifdOffset) + 8;
        Dictionary<ushort, object> values = [];
        for (ulong index = 0; index < entryCount; index++)
        {
            int entryOffset = checked(entriesOffset + ((int)index * 20));
            if (entryOffset + 20 > bytes.Length)
            {
                break;
            }

            TryReadBigTiffEntry(bytes, entryOffset, littleEndian, values);
        }

        return ToSnapshot(values);
    }

    private static async Task<GeoTiffTagSnapshot?> TryReadClassicAsync(
        Stream stream,
        bool littleEndian,
        uint ifdOffset,
        CancellationToken cancellationToken)
    {
        if (ifdOffset + 2 > stream.Length)
        {
            return null;
        }

        byte[] entryCountBytes = new byte[2];
        if (!await TryReadExactAsync(stream, ifdOffset, entryCountBytes, cancellationToken))
        {
            return null;
        }

        ushort entryCount = ReadUInt16(entryCountBytes, 0, littleEndian);
        Dictionary<ushort, object> values = [];
        byte[] entryBytes = new byte[12];
        for (int index = 0; index < entryCount; index++)
        {
            long entryOffset = checked((long)ifdOffset + 2 + (index * 12L));
            if (entryOffset + entryBytes.Length > stream.Length)
            {
                break;
            }

            if (!await TryReadExactAsync(stream, entryOffset, entryBytes, cancellationToken))
            {
                break;
            }

            await TryReadClassicEntryAsync(stream, entryBytes, littleEndian, values, cancellationToken);
        }

        return ToSnapshot(values);
    }

    private static async Task<GeoTiffTagSnapshot?> TryReadBigTiffAsync(
        Stream stream,
        bool littleEndian,
        CancellationToken cancellationToken)
    {
        byte[] header = new byte[16];
        if (!await TryReadExactAsync(stream, 0, header, cancellationToken))
        {
            return null;
        }

        if (ReadUInt16(header, 4, littleEndian) != 8 || ReadUInt16(header, 6, littleEndian) != 0)
        {
            return null;
        }

        ulong ifdOffset = ReadUInt64(header, 8, littleEndian);
        if (ifdOffset + 8 > (ulong)stream.Length)
        {
            return null;
        }

        byte[] entryCountBytes = new byte[8];
        if (!await TryReadExactAsync(stream, checked((long)ifdOffset), entryCountBytes, cancellationToken))
        {
            return null;
        }

        ulong entryCount = ReadUInt64(entryCountBytes, 0, littleEndian);
        Dictionary<ushort, object> values = [];
        byte[] entryBytes = new byte[20];
        for (ulong index = 0; index < entryCount; index++)
        {
            long entryOffset = checked((long)ifdOffset + 8 + checked((long)index * entryBytes.Length));
            if (entryOffset + entryBytes.Length > stream.Length)
            {
                break;
            }

            if (!await TryReadExactAsync(stream, entryOffset, entryBytes, cancellationToken))
            {
                break;
            }

            await TryReadBigTiffEntryAsync(stream, entryBytes, littleEndian, values, cancellationToken);
        }

        return ToSnapshot(values);
    }

    private static void TryReadClassicEntry(
        ReadOnlySpan<byte> bytes,
        int entryOffset,
        bool littleEndian,
        Dictionary<ushort, object> values)
    {
        ushort tag = ReadUInt16(bytes, entryOffset, littleEndian);
        if (!IsTrackedTag(tag))
        {
            return;
        }

        ushort type = ReadUInt16(bytes, entryOffset + 2, littleEndian);
        uint count = ReadUInt32(bytes, entryOffset + 4, littleEndian);
        uint valueOrOffset = ReadUInt32(bytes, entryOffset + 8, littleEndian);
        if (!TryReadEntryValue(bytes, type, count, valueOrOffset, bytes.Slice(entryOffset + 8, 4), littleEndian, out object? value))
        {
            return;
        }

        values[tag] = value!;
    }

    private static async Task TryReadClassicEntryAsync(
        Stream stream,
        ReadOnlyMemory<byte> entryBytes,
        bool littleEndian,
        Dictionary<ushort, object> values,
        CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> entrySpan = entryBytes.Span;
        ushort tag = ReadUInt16(entrySpan, 0, littleEndian);
        if (!IsTrackedTag(tag))
        {
            return;
        }

        ushort type = ReadUInt16(entrySpan, 2, littleEndian);
        uint count = ReadUInt32(entrySpan, 4, littleEndian);
        uint valueOrOffset = ReadUInt32(entrySpan, 8, littleEndian);
        object? value = await TryReadEntryValueAsync(
            stream,
            type,
            count,
            valueOrOffset,
            entryBytes.Slice(8, 4),
            littleEndian,
            cancellationToken);
        if (value is null)
        {
            return;
        }

        values[tag] = value;
    }

    private static void TryReadBigTiffEntry(
        ReadOnlySpan<byte> bytes,
        int entryOffset,
        bool littleEndian,
        Dictionary<ushort, object> values)
    {
        ushort tag = ReadUInt16(bytes, entryOffset, littleEndian);
        if (!IsTrackedTag(tag))
        {
            return;
        }

        ushort type = ReadUInt16(bytes, entryOffset + 2, littleEndian);
        ulong count = ReadUInt64(bytes, entryOffset + 4, littleEndian);
        ulong valueOrOffset = ReadUInt64(bytes, entryOffset + 12, littleEndian);
        if (!TryReadEntryValue(bytes, type, count, valueOrOffset, bytes.Slice(entryOffset + 12, 8), littleEndian, out object? value))
        {
            return;
        }

        values[tag] = value!;
    }

    private static async Task TryReadBigTiffEntryAsync(
        Stream stream,
        ReadOnlyMemory<byte> entryBytes,
        bool littleEndian,
        Dictionary<ushort, object> values,
        CancellationToken cancellationToken)
    {
        ReadOnlySpan<byte> entrySpan = entryBytes.Span;
        ushort tag = ReadUInt16(entrySpan, 0, littleEndian);
        if (!IsTrackedTag(tag))
        {
            return;
        }

        ushort type = ReadUInt16(entrySpan, 2, littleEndian);
        ulong count = ReadUInt64(entrySpan, 4, littleEndian);
        ulong valueOrOffset = ReadUInt64(entrySpan, 12, littleEndian);
        object? value = await TryReadEntryValueAsync(
            stream,
            type,
            count,
            valueOrOffset,
            entryBytes.Slice(12, 8),
            littleEndian,
            cancellationToken);
        if (value is null)
        {
            return;
        }

        values[tag] = value;
    }

    private static bool TryReadEntryValue(
        ReadOnlySpan<byte> bytes,
        ushort type,
        ulong count,
        ulong valueOrOffset,
        ReadOnlySpan<byte> inlineValueBytes,
        bool littleEndian,
        out object? value)
    {
        value = null;
        int typeSize = GetTypeSize(type);
        if (typeSize == 0 || count == 0 || count > int.MaxValue)
        {
            return false;
        }

        ulong byteLength = checked((ulong)typeSize * count);
        ReadOnlySpan<byte> rawValueBytes;
        if (byteLength <= (ulong)inlineValueBytes.Length)
        {
            rawValueBytes = inlineValueBytes[..(int)byteLength];
        }
        else
        {
            if (valueOrOffset > int.MaxValue || valueOrOffset + byteLength > (ulong)bytes.Length)
            {
                return false;
            }

            rawValueBytes = bytes.Slice((int)valueOrOffset, (int)byteLength);
        }

        value = type switch
        {
            TypeShort => ReadUInt16Array(rawValueBytes, (int)count, littleEndian),
            TypeLong => ReadUInt32Array(rawValueBytes, (int)count, littleEndian),
            TypeDouble => ReadDoubleArray(rawValueBytes, (int)count, littleEndian),
            TypeAscii => Encoding.ASCII.GetString(rawValueBytes).TrimEnd('\0'),
            _ => null,
        };
        return value is not null;
    }

    private static async Task<object?> TryReadEntryValueAsync(
        Stream stream,
        ushort type,
        ulong count,
        ulong valueOrOffset,
        ReadOnlyMemory<byte> inlineValueBytes,
        bool littleEndian,
        CancellationToken cancellationToken)
    {
        int typeSize = GetTypeSize(type);
        if (typeSize == 0 || count == 0 || count > int.MaxValue)
        {
            return null;
        }

        ulong byteLength = checked((ulong)typeSize * count);
        byte[] rawValueBytes;
        if (byteLength <= (ulong)inlineValueBytes.Length)
        {
            rawValueBytes = inlineValueBytes[..checked((int)byteLength)].ToArray();
        }
        else
        {
            if (byteLength > int.MaxValue
                || valueOrOffset > (ulong)stream.Length
                || valueOrOffset + byteLength > (ulong)stream.Length)
            {
                return null;
            }

            rawValueBytes = new byte[checked((int)byteLength)];
            if (!await TryReadExactAsync(stream, checked((long)valueOrOffset), rawValueBytes, cancellationToken))
            {
                return null;
            }
        }

        return type switch
        {
            TypeShort => ReadUInt16Array(rawValueBytes, (int)count, littleEndian),
            TypeLong => ReadUInt32Array(rawValueBytes, (int)count, littleEndian),
            TypeDouble => ReadDoubleArray(rawValueBytes, (int)count, littleEndian),
            TypeAscii => Encoding.ASCII.GetString(rawValueBytes).TrimEnd('\0'),
            _ => null,
        };
    }

    private static bool IsTrackedTag(ushort tag)
    {
        return tag is PixelScaleTag
            or ModelTiePointTag
            or ModelTransformTag
            or GeoKeyDirectoryTag
            or GeoDoubleParamsTag
            or GeoAsciiParamsTag;
    }

    private static async Task<bool> TryReadExactAsync(
        Stream stream,
        long offset,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer[totalRead..], cancellationToken);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    private static GeoTiffTagSnapshot? ToSnapshot(Dictionary<ushort, object> values)
    {
        values.TryGetValue(ModelTiePointTag, out object? modelTiePoint);
        values.TryGetValue(PixelScaleTag, out object? pixelScale);
        values.TryGetValue(ModelTransformTag, out object? modelTransform);
        values.TryGetValue(GeoKeyDirectoryTag, out object? geoKeyDirectory);
        values.TryGetValue(GeoDoubleParamsTag, out object? geoDoubleParams);
        values.TryGetValue(GeoAsciiParamsTag, out object? geoAsciiParams);

        if (modelTiePoint is null
            && pixelScale is null
            && modelTransform is null
            && geoKeyDirectory is null
            && geoDoubleParams is null
            && geoAsciiParams is null)
        {
            return null;
        }

        return new GeoTiffTagSnapshot(
            ConvertToDoubleArray(modelTiePoint),
            ConvertToDoubleArray(pixelScale),
            ConvertToDoubleArray(modelTransform),
            ConvertToUInt16Array(geoKeyDirectory),
            ConvertToDoubleArray(geoDoubleParams),
            geoAsciiParams as string);
    }

    private static double[]? ConvertToDoubleArray(object? value)
    {
        return value switch
        {
            double[] doubles => doubles,
            uint[] uints => uints.Select(static item => (double)item).ToArray(),
            ushort[] ushorts => ushorts.Select(static item => (double)item).ToArray(),
            _ => null,
        };
    }

    private static ushort[]? ConvertToUInt16Array(object? value)
    {
        return value switch
        {
            ushort[] ushorts => ushorts,
            uint[] uints => uints.Select(static item => checked((ushort)item)).ToArray(),
            _ => null,
        };
    }

    private static int GetTypeSize(ushort type)
    {
        return type switch
        {
            TypeAscii => 1,
            TypeShort => 2,
            TypeLong => 4,
            TypeDouble => 8,
            _ => 0,
        };
    }

    private static ushort[] ReadUInt16Array(ReadOnlySpan<byte> bytes, int count, bool littleEndian)
    {
        ushort[] values = new ushort[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = ReadUInt16(bytes, index * 2, littleEndian);
        }

        return values;
    }

    private static uint[] ReadUInt32Array(ReadOnlySpan<byte> bytes, int count, bool littleEndian)
    {
        uint[] values = new uint[count];
        for (int index = 0; index < count; index++)
        {
            values[index] = ReadUInt32(bytes, index * 4, littleEndian);
        }

        return values;
    }

    private static double[] ReadDoubleArray(ReadOnlySpan<byte> bytes, int count, bool littleEndian)
    {
        double[] values = new double[count];
        for (int index = 0; index < count; index++)
        {
            ulong rawBits = ReadUInt64(bytes, index * 8, littleEndian);
            values[index] = BitConverter.Int64BitsToDouble(unchecked((long)rawBits));
        }

        return values;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset, bool littleEndian)
    {
        ReadOnlySpan<byte> slice = bytes.Slice(offset, 2);
        return littleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(slice)
            : BinaryPrimitives.ReadUInt16BigEndian(slice);
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, int offset, bool littleEndian)
    {
        ReadOnlySpan<byte> slice = bytes.Slice(offset, 4);
        return littleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(slice)
            : BinaryPrimitives.ReadUInt32BigEndian(slice);
    }

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, int offset, bool littleEndian)
    {
        ReadOnlySpan<byte> slice = bytes.Slice(offset, 8);
        return littleEndian
            ? BinaryPrimitives.ReadUInt64LittleEndian(slice)
            : BinaryPrimitives.ReadUInt64BigEndian(slice);
    }
}
