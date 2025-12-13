// Copyright Carl Reinke
//
// This file is part of a library that is licensed under the terms of the GNU
// Lesser General Public License Version 3 as published by the Free Software
// Foundation.
//
// This license does not grant rights under trademark law for use of any trade
// names, trademarks, or service marks.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Xunit;
using GraphicControlExtensionFields = (Tetractic.Formats.Gif.GifDisposalMethod DisposalMethod, bool UserInput, bool HasTransparentColor, ushort DelayTime, byte TransparentColorIndex, byte Reserved);
using ImageDescriptorFields = (ushort Left, ushort Top, ushort Width, ushort Height, bool HasLocalColorTable, bool Interlaced, bool Sorted, byte LocalColorTableSize, byte Reserved);
using LogicalScreenDescriptorFields = (ushort Width, ushort Height, bool HasGlobalColorTable, byte ColorResolution, bool Sorted, byte GlobalColorTableSize, byte BackgroundColorIndex, byte PixelAspectRatio);
using PlainTextExtensionFields = (ushort Left, ushort Top, ushort Width, ushort Height, byte CellWidth, byte CellHeight, byte ForegroundColorIndex, byte BackgroundColorIndex);

namespace Tetractic.Formats.Gif.Tests;

public class GifWriterTests
{
    [Fact]
    public static void Constructor_StreamIsNull_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new GifWriter(null!));

        Assert.Equal("stream", ex.ParamName);
    }

    [Fact]
    public static void Constructor_StreamCannotWrite_ThrowsArgumentException()
    {
        using (var stream = new UnwritableStream())
        {
            var ex = Assert.Throws<ArgumentException>(() => new GifWriter(stream));

            Assert.Equal("stream", ex.ParamName);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public static void Constructor_LeaveOpen_StreamIsDisposedOrNot(bool leaveOpen)
    {
        using (var stream = new MemoryStream())
        {
            new GifWriter(stream, leaveOpen).Dispose();

            Assert.Equal(leaveOpen, stream.CanWrite);
        }
    }

    [Fact]
    public static void WriteHeader_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version87a);

            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteHeader(GifVersion.Version87a));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Theory]
    [InlineData((GifVersion)(-1))]
    [InlineData((GifVersion)(100 * 26))]
    public static void WriteHeader_InvalidVersion_ThrowsArgumentException(GifVersion version)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<ArgumentException>(() => writer.WriteHeader(version));

            Assert.Equal("version", ex.ParamName);
        }
    }

    [Theory]
    [InlineData(GifVersion.Version87a, new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 })]              // "GIF87a"
    [InlineData(GifVersion.Version89a, new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 })]              // "GIF89a"
    [InlineData((GifVersion)((99 - 87) * 26 + 25), new byte[] { 0x47, 0x49, 0x46, 0x39, 0x39, 0x7A })]  // "GIF99z"
    [InlineData((GifVersion)((100 - 87) * 26), new byte[] { 0x47, 0x49, 0x46, 0x30, 0x30, 0x61 })]      // "GIF00a"
    [InlineData((GifVersion)(100 * 26 - 1), new byte[] { 0x47, 0x49, 0x46, 0x38, 0x36, 0x7A })]         // "GIF86z"
    public static void WriteHeader_ValidVersion_WritesExpectedBytes(GifVersion version, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(version);

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Fact]
    public static void WriteLogicalScreenDescriptor_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteLogicalScreenDescriptor(default));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    public static readonly TheoryData<LogicalScreenDescriptorFields, byte[], string> WriteLogicalScreenDescriptor_ValidInVersion87a_Data =
    [
        .. GifReaderTests.ReadLogicalScreenDescriptor_ValidInVersion87a_Data
            .Select(static x => new TheoryDataRow<LogicalScreenDescriptorFields, byte[], string>(x.Data.Item2, x.Data.Item1, x.Data.Item3))
    ];

    public static readonly TheoryData<LogicalScreenDescriptorFields, byte[], string> WriteLogicalScreenDescriptor_ValidInVersion89a_Data =
    [
        .. GifReaderTests.ReadLogicalScreenDescriptor_ValidInVersion89a_Data
            .Select(static x => new TheoryDataRow<LogicalScreenDescriptorFields, byte[], string>(x.Data.Item2, x.Data.Item1, x.Data.Item3))
    ];

    [Theory]
    [MemberData(nameof(WriteLogicalScreenDescriptor_ValidInVersion89a_Data))]
    public static void WriteLogicalScreenDescriptor_InvalidInVersion87a_ThrowsInvalidOperationException(LogicalScreenDescriptorFields fields, byte[] expectedBytes, string expectedMessage)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version87a);

            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteLogicalScreenDescriptor(CreateLogicalScreenDescriptor(fields)));

            Assert.Equal(expectedMessage, ex.Message);
        }

        _ = expectedBytes;
    }

    [Theory]
    [MemberData(nameof(WriteLogicalScreenDescriptor_ValidInVersion87a_Data))]
    public static void WriteLogicalScreenDescriptor_ValidInVersion87a_WritesExpectedBytes(LogicalScreenDescriptorFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteLogicalScreenDescriptor_ValidInVersion_WritesExpectedBytes(GifVersion.Version87a, fields, expectedBytes);

        _ = expectedMessage;
    }

    [Theory]
    [MemberData(nameof(WriteLogicalScreenDescriptor_ValidInVersion87a_Data))]
    [MemberData(nameof(WriteLogicalScreenDescriptor_ValidInVersion89a_Data))]
    public static void WriteLogicalScreenDescriptor_ValidInVersion89a_WritesExpectedBytes(LogicalScreenDescriptorFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteLogicalScreenDescriptor_ValidInVersion_WritesExpectedBytes(GifVersion.Version89a, fields, expectedBytes);

        _ = expectedMessage;
    }

    private static void WriteLogicalScreenDescriptor_ValidInVersion_WritesExpectedBytes(GifVersion version, LogicalScreenDescriptorFields fields, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(version);

            stream.SetLength(0);

            writer.WriteLogicalScreenDescriptor(CreateLogicalScreenDescriptor(fields));

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Fact]
    public static void WriteColorTable_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteColorTable(default));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Theory]
    [InlineData(0, (2 << 0) + 1)]
    [InlineData(1, (2 << 1) + 1)]
    [InlineData(2, (2 << 2) + 1)]
    [InlineData(3, (2 << 3) + 1)]
    [InlineData(4, (2 << 4) + 1)]
    [InlineData(5, (2 << 5) + 1)]
    [InlineData(6, (2 << 6) + 1)]
    [InlineData(7, (2 << 7) + 1)]
    public static void WriteColorTable_GlobalColorTableTooManyColors_ThrowsInvalidOperationException(byte colorTableSize, int colorsCount)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version87a);

            writer.WriteLogicalScreenDescriptor(new GifLogicalScreenDescriptor
            {
                HasGlobalColorTable = true,
                GlobalColorTableSize = colorTableSize,
            });

            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteColorTable(new GifColor[colorsCount]));

            Assert.Equal("Too many colors.", ex.Message);
        }
    }

    [Theory]
    [InlineData(0, (2 << 0) + 1)]
    [InlineData(1, (2 << 1) + 1)]
    [InlineData(2, (2 << 2) + 1)]
    [InlineData(3, (2 << 3) + 1)]
    [InlineData(4, (2 << 4) + 1)]
    [InlineData(5, (2 << 5) + 1)]
    [InlineData(6, (2 << 6) + 1)]
    [InlineData(7, (2 << 7) + 1)]
    public static void WriteColorTable_LocalColorTableTooManyColors_ThrowsInvalidOperationException(byte colorTableSize, int colorsCount)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version87a);

            writer.WriteLogicalScreenDescriptor(default);

            writer.WriteImageDescriptor(new GifImageDescriptor
            {
                HasLocalColorTable = true,
                LocalColorTableSize = colorTableSize,
            });

            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteColorTable(new GifColor[colorsCount]));

            Assert.Equal("Too many colors.", ex.Message);
        }
    }

    [Theory]
    [InlineData(0, new byte[0], new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0, new byte[] { 0x01, 0x02, 0x03 }, new byte[] { 0x01, 0x02, 0x03, 0x00, 0x00, 0x00 })]
    [InlineData(0, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 })]
    [InlineData(1, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C }, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C })]
    public static void WriteColorTable_GlobalColorTable_WritesExpectedBytes(byte colorTableSize, byte[] colorComponents, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version87a);

            writer.WriteLogicalScreenDescriptor(new GifLogicalScreenDescriptor
            {
                HasGlobalColorTable = true,
                GlobalColorTableSize = colorTableSize,
            });

            var colors = new GifColor[colorComponents.Length / 3];
            for (int i = 0; i < colors.Length; ++i)
                colors[i] = new GifColor(
                    r: colorComponents[i * 3 + 0],
                    g: colorComponents[i * 3 + 1],
                    b: colorComponents[i * 3 + 2]);

            stream.SetLength(0);

            writer.WriteColorTable(colors);

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Theory]
    [InlineData(0, new byte[0], new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0, new byte[] { 0x01, 0x02, 0x03 }, new byte[] { 0x01, 0x02, 0x03, 0x00, 0x00, 0x00 })]
    [InlineData(0, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 }, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 })]
    [InlineData(1, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C }, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C })]
    public static void WriteColorTable_LocalColorTable_WritesExpectedBytes(byte colorTableSize, byte[] colorComponents, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version87a);

            writer.WriteLogicalScreenDescriptor(default);

            writer.WriteImageDescriptor(new GifImageDescriptor
            {
                HasLocalColorTable = true,
                LocalColorTableSize = colorTableSize,
            });

            var colors = new GifColor[colorComponents.Length / 3];
            for (int i = 0; i < colors.Length; ++i)
                colors[i] = new GifColor(
                    r: colorComponents[i * 3 + 0],
                    g: colorComponents[i * 3 + 1],
                    b: colorComponents[i * 3 + 2]);

            stream.SetLength(0);

            writer.WriteColorTable(colors);

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Fact]
    public static void WriteImageDescriptor_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteImageDescriptor(default));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    public static readonly TheoryData<ImageDescriptorFields, byte[], string> WriteImageDescriptor_ValidInVersion87a_Data =
    [
        .. GifReaderTests.ReadImageDescriptor_ValidInVersion87a_Data
            .Select(static x => new TheoryDataRow<ImageDescriptorFields, byte[], string>(x.Data.Item2, x.Data.Item1, x.Data.Item3))
    ];

    public static readonly TheoryData<ImageDescriptorFields, byte[], string> WriteImageDescriptor_ValidInVersion89a_Data =
    [
        .. GifReaderTests.ReadImageDescriptor_ValidInVersion89a_Data
            .Select(static x => new TheoryDataRow<ImageDescriptorFields, byte[], string>(x.Data.Item2, x.Data.Item1, x.Data.Item3))
    ];

    public static readonly TheoryData<ImageDescriptorFields, byte[], string> WriteImageDescriptor_ValidInUnknownVersion_Data =
    [
        .. GifReaderTests.ReadImageDescriptor_ValidInUnknownVersion_Data
            .Select(static x => new TheoryDataRow<ImageDescriptorFields, byte[], string>(x.Data.Item2, x.Data.Item1, x.Data.Item3))
    ];

    [Theory]
    [MemberData(nameof(WriteImageDescriptor_ValidInVersion89a_Data))]
    [MemberData(nameof(WriteImageDescriptor_ValidInUnknownVersion_Data))]
    public static void WriteImageDescriptor_InvalidInVersion87a_ThrowsInvalidOperationException(ImageDescriptorFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteImageDescriptor_InvalidInVersion_ThrowsInvalidOperationException(GifVersion.Version87a, fields, expectedBytes, expectedMessage);
    }

    [Theory]
    [MemberData(nameof(WriteImageDescriptor_ValidInUnknownVersion_Data))]
    public static void WriteImageDescriptor_InvalidInVersion89a_ThrowsInvalidOperationException(ImageDescriptorFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteImageDescriptor_InvalidInVersion_ThrowsInvalidOperationException(GifVersion.Version89a, fields, expectedBytes, expectedMessage);
    }

    private static void WriteImageDescriptor_InvalidInVersion_ThrowsInvalidOperationException(GifVersion version, ImageDescriptorFields fields, byte[] expectedBytes, string expectedMessage)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(version);

            writer.WriteLogicalScreenDescriptor(default);

            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteImageDescriptor(CreateImageDescriptor(fields)));

            Assert.Equal(expectedMessage, ex.Message);
        }

        _ = expectedBytes;
    }

    [Theory]
    [MemberData(nameof(WriteImageDescriptor_ValidInVersion87a_Data))]
    public static void WriteImageDescriptor_ValidInVersion87a_WritesExpectedBytes(ImageDescriptorFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteImageDescriptor_ValidInVersion_WritesExpectedBytes(GifVersion.Version87a, fields, expectedBytes);

        _ = expectedMessage;
    }

    [Theory]
    [MemberData(nameof(WriteImageDescriptor_ValidInVersion87a_Data))]
    [MemberData(nameof(WriteImageDescriptor_ValidInVersion89a_Data))]
    public static void WriteImageDescriptor_ValidInVersion89a_WritesExpectedBytes(ImageDescriptorFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteImageDescriptor_ValidInVersion_WritesExpectedBytes(GifVersion.Version89a, fields, expectedBytes);

        _ = expectedMessage;
    }

    [Theory]
    [MemberData(nameof(WriteImageDescriptor_ValidInVersion87a_Data))]
    [MemberData(nameof(WriteImageDescriptor_ValidInVersion89a_Data))]
    [MemberData(nameof(WriteImageDescriptor_ValidInUnknownVersion_Data))]
    public static void WriteImageDescriptor_ValidInUnknownVersion_WritesExpectedBytes(ImageDescriptorFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteImageDescriptor_ValidInVersion_WritesExpectedBytes(GifVersion.Version89a + 1, fields, expectedBytes);

        _ = expectedMessage;
    }

    private static void WriteImageDescriptor_ValidInVersion_WritesExpectedBytes(GifVersion version, ImageDescriptorFields fields, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(version);

            writer.WriteLogicalScreenDescriptor(default);

            stream.SetLength(0);

            writer.WriteImageDescriptor(CreateImageDescriptor(fields));

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Fact]
    public static void WriteImageData_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteImageData(default));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 0, 1)]
    [InlineData(0, 1, 1)]
    public static void WriteImageData_MismatchedImageDataLength_ThrowsInvalidOperationException(ushort width, ushort height, int imageDataLength)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version87a);

            writer.WriteLogicalScreenDescriptor(default);

            writer.WriteImageDescriptor(new GifImageDescriptor
            {
                Width = width,
                Height = height,
            });

            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteImageData(new byte[imageDataLength]));

            Assert.Equal("Invalid image data length for image dimensions.", ex.Message);
        }
    }

    [Theory]
    [InlineData(0x00)]
    [InlineData(0xFF)]
    public static void WriteImageData_MaximallyCompressible_WritesDataThatCanBeRead(byte value)
    {
        byte[] imageData = new byte[4096 * 4096];
        imageData.AsSpan().Fill(value);

        WriteImageData_Valid_WritesDataThatCanBeRead(4096, 4096, imageData);
    }

    [Theory]
    [InlineData(0x01)]
    [InlineData(0x03)]
    [InlineData(0x07)]
    [InlineData(0x0F)]
    [InlineData(0xFF)]
    public static void WriteImageData_HighlyCompressible_WritesDataThatCanBeRead(byte mask)
    {
        byte[] imageData = new byte[4096 * 2];
        for (int i = 0; i < imageData.Length; ++i)
            imageData[i] = (byte)(i & mask);

        WriteImageData_Valid_WritesDataThatCanBeRead(4096, 2, imageData);
    }

    [Fact]
    public static void WriteImageData_Uncompressible_WritesDataThatCanBeRead()
    {
        var random = new Random(0);
        byte[] imageData = new byte[2 * 4096];
        random.NextBytes(imageData);

        WriteImageData_Valid_WritesDataThatCanBeRead(2, 4096, imageData);
    }

    private static void WriteImageData_Valid_WritesDataThatCanBeRead(ushort width, ushort height, byte[] imageData)
    {
        var stream = new MemoryStream();

        using (var writer = new GifWriter(stream, leaveOpen: true))
        {
            writer.WriteHeader(GifVersion.Version87a);

            writer.WriteLogicalScreenDescriptor(default);

            writer.WriteImageDescriptor(new GifImageDescriptor
            {
                Width = width,
                Height = height,
            });

            writer.WriteImageData(imageData);
        }

        stream.Position = 0;

        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            byte[] readImageData = reader.ReadImageData();

            Assert.Equal(imageData, readImageData);
        }
    }

    [Fact]
    public static void WriteExtensionLabel_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteExtensionLabel(GifExtensionLabel.Comment));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    public static readonly TheoryData<GifExtensionLabel, byte> WriteExtensionLabel_ValidInVersion89a_Data = new()
    {
        { GifExtensionLabel.PlainText, (byte)GifExtensionLabel.PlainText },
        { GifExtensionLabel.GraphicControl, (byte)GifExtensionLabel.GraphicControl },
        { GifExtensionLabel.Comment, (byte)GifExtensionLabel.Comment },
        { GifExtensionLabel.Application, (byte)GifExtensionLabel.Application },
    };

    public static readonly TheoryData<GifExtensionLabel, byte> WriteExtensionLabel_ValidInUnknownVersion_Data = new()
    {
        { 0x00, 0x00 },
    };

    [Theory]
    [MemberData(nameof(WriteExtensionLabel_ValidInVersion89a_Data))]
    [MemberData(nameof(WriteExtensionLabel_ValidInUnknownVersion_Data))]
    public static void WriteExtensionLabel_InvalidInVersion87a_ThrowsInvalidOperationException(GifExtensionLabel label, byte expectedByte)
    {
        WriteExtensionLabel_InvalidInVersion_ThrowsInvalidOperationException(GifVersion.Version87a, label, expectedByte);
    }

    [Theory]
    [MemberData(nameof(WriteExtensionLabel_ValidInUnknownVersion_Data))]
    public static void WriteExtensionLabel_InvalidInVersion89a_ThrowsInvalidOperationException(GifExtensionLabel label, byte expectedByte)
    {
        WriteExtensionLabel_InvalidInVersion_ThrowsInvalidOperationException(GifVersion.Version89a, label, expectedByte);
    }

    private static void WriteExtensionLabel_InvalidInVersion_ThrowsInvalidOperationException(GifVersion version, GifExtensionLabel label, byte expectedByte)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(version);

            writer.WriteLogicalScreenDescriptor(default);

            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteExtensionLabel(label));

            Assert.Equal("Not valid for format version.", ex.Message);
        }

        _ = expectedByte;
    }

    [Theory]
    [MemberData(nameof(WriteExtensionLabel_ValidInVersion89a_Data))]
    public static void WriteExtensionLabel_ValidInVersion89a_WritesExpectedBytes(GifExtensionLabel label, byte expectedByte)
    {
        WriteExtensionLabel_ValidInVersion_WritesExpectedBytes(GifVersion.Version89a, label, expectedByte);
    }

    [Theory]
    [MemberData(nameof(WriteExtensionLabel_ValidInVersion89a_Data))]
    [MemberData(nameof(WriteExtensionLabel_ValidInUnknownVersion_Data))]
    public static void WriteExtensionLabel_ValidInUnknownVersion_WritesExpectedBytes(GifExtensionLabel label, byte expectedByte)
    {
        WriteExtensionLabel_ValidInVersion_WritesExpectedBytes(GifVersion.Version89a + 1, label, expectedByte);
    }

    private static void WriteExtensionLabel_ValidInVersion_WritesExpectedBytes(GifVersion version, GifExtensionLabel label, byte expectedByte)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(version);

            writer.WriteLogicalScreenDescriptor(default);

            stream.SetLength(0);

            writer.WriteExtensionLabel(label);

            Assert.Equal([0x21, expectedByte], stream.ToArray());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(256)]
    public static void WriteSubblock_InvalidLength_ThrowsArgumentException(int blockLength)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<ArgumentException>(() => writer.WriteSubblock(new byte[blockLength]));

            Assert.Equal("data", ex.ParamName);
        }
    }

    [Fact]
    public static void WriteSubblock_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteSubblock(new byte[1]));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    private static readonly IEnumerable<byte> _255bytes = Enumerable.Range(0, 255).Select(x => (byte)x);

    public static readonly TheoryData<byte[], byte[]> WriteSubblock_Data = new()
    {
        { new byte[] { 0x00 }, new byte[] { 0x01, 0x00 } },
        { new byte[] { 0x01, 0x02 }, new byte[] { 0x02, 0x01, 0x02 } },
        { [.. _255bytes], [0xFF, .. _255bytes] },
    };

    [Theory]
    [MemberData(nameof(WriteSubblock_Data))]
    public static void WriteSubblock_AfterWriteExtensionLabel_WritesExpectedBytes(byte[] data, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            writer.WriteExtensionLabel(GifExtensionLabel.Comment);

            stream.SetLength(0);

            writer.WriteSubblock(data);

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(WriteSubblock_Data))]
    public static void WriteSubblock_AfterWriteSubblock_WritesExpectedBytes(byte[] data, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            writer.WriteExtensionLabel(GifExtensionLabel.Comment);

            writer.WriteSubblock([0x00]);

            stream.SetLength(0);

            writer.WriteSubblock(data);

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(WriteSubblock_Data))]
    public static void WriteSubblock_AfterWritePlainTextExtension_WritesExpectedBytes(byte[] data, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            writer.WritePlainTextExtension(default);

            stream.SetLength(0);

            writer.WriteSubblock(data);

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(WriteSubblock_Data))]
    public static void WriteSubblock_AfterWriteGraphicControlExtension_WritesExpectedBytes(byte[] data, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            writer.WriteGraphicControlExtension(default);

            stream.SetLength(0);

            writer.WriteSubblock(data);

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Theory]
    [MemberData(nameof(WriteSubblock_Data))]
    public static void WriteSubblock_AfterWriteApplicationExtension_WritesExpectedBytes(byte[] data, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            writer.WriteApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            stream.SetLength(0);

            writer.WriteSubblock(data);

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Fact]
    public static void WriteBlockTerminator_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(writer.WriteBlockTerminator);

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void WriteBlockTerminator_Valid_WritesExpectedBytes()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            writer.WriteExtensionLabel(GifExtensionLabel.Comment);

            stream.SetLength(0);

            writer.WriteBlockTerminator();

            Assert.Equal([0x00], stream.ToArray());
        }
    }

    [Fact]
    public static void WriteGraphicControlExtension_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteGraphicControlExtension(default));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    public static readonly TheoryData<GraphicControlExtensionFields, byte[], string> WriteGraphicControlExtension_ValidInVersion89a_Data =
    [
        .. GifReaderTests.ReadGraphicControlExtension_ValidInVersion89a_Data
            .Select(static x => new TheoryDataRow<GraphicControlExtensionFields, byte[], string>(x.Data.Item2, x.Data.Item1, x.Data.Item3))
    ];

    public static readonly TheoryData<GraphicControlExtensionFields, byte[], string> WriteGraphicControlExtension_ValidInUnknownVersion_Data =
    [
        .. GifReaderTests.ReadGraphicControlExtension_ValidInUnknownVersion_Data
            .Select(static x => new TheoryDataRow<GraphicControlExtensionFields, byte[], string>(x.Data.Item2, x.Data.Item1, x.Data.Item3))
    ];

    [Theory]
    [MemberData(nameof(WriteGraphicControlExtension_ValidInVersion89a_Data))]
    [MemberData(nameof(WriteGraphicControlExtension_ValidInUnknownVersion_Data))]
    public static void WriteGraphicControlExtension_InvalidInVersion87a_ThrowsInvalidOperationException(GraphicControlExtensionFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteGraphicControlExtension_InvalidInVersion_ThrowsInvalidOperationException(fields, expectedBytes, GifVersion.Version87a, "Not valid for format version.");

        _ = expectedMessage;
    }

    [Theory]
    [MemberData(nameof(WriteGraphicControlExtension_ValidInUnknownVersion_Data))]
    public static void WriteGraphicControlExtension_InvalidInVersion89a_ThrowsInvalidOperationException(GraphicControlExtensionFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteGraphicControlExtension_InvalidInVersion_ThrowsInvalidOperationException(fields, expectedBytes, GifVersion.Version89a, expectedMessage);
    }

    private static void WriteGraphicControlExtension_InvalidInVersion_ThrowsInvalidOperationException(GraphicControlExtensionFields fields, byte[] expectedBytes, GifVersion version, string expectedMessage)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(version);

            writer.WriteLogicalScreenDescriptor(default);

            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteGraphicControlExtension(CreateGraphicControlExtension(fields)));

            Assert.Equal(expectedMessage, ex.Message);
        }

        _ = expectedBytes;
    }

    [Theory]
    [MemberData(nameof(WriteGraphicControlExtension_ValidInVersion89a_Data))]
    public static void WriteGraphicControlExtension_ValidInVersion89a_WritesExpectedBytes(GraphicControlExtensionFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteGraphicControlExtension_ValidInVersion_WritesExpectedBytes(GifVersion.Version89a, fields, expectedBytes);

        _ = expectedMessage;
    }

    [Theory]
    [MemberData(nameof(WriteGraphicControlExtension_ValidInVersion89a_Data))]
    [MemberData(nameof(WriteGraphicControlExtension_ValidInUnknownVersion_Data))]
    public static void WriteGraphicControlExtension_ValidInUnknownVersion_WritesExpectedBytes(GraphicControlExtensionFields fields, byte[] expectedBytes, string expectedMessage)
    {
        WriteGraphicControlExtension_ValidInVersion_WritesExpectedBytes(GifVersion.Version89a + 1, fields, expectedBytes);

        _ = expectedMessage;
    }

    private static void WriteGraphicControlExtension_ValidInVersion_WritesExpectedBytes(GifVersion version, GraphicControlExtensionFields fields, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(version);

            writer.WriteLogicalScreenDescriptor(default);

            stream.SetLength(0);

            writer.WriteGraphicControlExtension(CreateGraphicControlExtension(fields));

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Fact]
    public static void WritePlainTextExtension_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => writer.WritePlainTextExtension(default));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void WritePlainTextExtension_InvalidInVersion87a_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version87a);

            writer.WriteLogicalScreenDescriptor(default);

            var ex = Assert.Throws<InvalidOperationException>(() => writer.WritePlainTextExtension(default));

            Assert.Equal("Not valid for format version.", ex.Message);
        }
    }

    public static readonly TheoryData<PlainTextExtensionFields, byte[]> WritePlainTextExtension_ValidInVersion89a_Data =
    [
        .. GifReaderTests.ReadPlainTextExtension_Valid_Data
            .Select(static x => new TheoryDataRow<PlainTextExtensionFields, byte[]>(x.Data.Item2, x.Data.Item1))
    ];

    [Theory]
    [MemberData(nameof(WritePlainTextExtension_ValidInVersion89a_Data))]
    public static void WritePlainTextExtension_ValidInVersion89a_WritesExpectedBytes(PlainTextExtensionFields fields, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            stream.SetLength(0);

            writer.WritePlainTextExtension(CreatePlainTextExtension(fields));

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Fact]
    public static void WriteApplicationExtension_InvalidApplicationIdentifierLength_ThrowsArgumentException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<ArgumentException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[7];
                Span<byte> applicationAuthenticationCode = stackalloc byte[3];
                writer.WriteApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            Assert.Equal("applicationIdentifier", ex.ParamName);
        }
    }

    [Fact]
    public static void WriteApplicationExtension_InvalidApplicationAuthenticationCodeLength_ThrowsArgumentException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<ArgumentException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[8];
                Span<byte> applicationAuthenticationCode = stackalloc byte[2];
                writer.WriteApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            Assert.Equal("applicationAuthenticationCode", ex.ParamName);
        }
    }

    [Fact]
    public static void WriteApplicationExtension_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[8];
                Span<byte> applicationAuthenticationCode = stackalloc byte[3];
                writer.WriteApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void WriteApplicationExtension_InvalidInVersion87a_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version87a);

            writer.WriteLogicalScreenDescriptor(default);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[8];
                Span<byte> applicationAuthenticationCode = stackalloc byte[3];
                writer.WriteApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            Assert.Equal("Not valid for format version.", ex.Message);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, new byte[] { 0x00, 0x00, 0x00 }, new byte[] { 0x21, 0xFF, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, new byte[] { 0x09, 0x0A, 0x0B }, new byte[] { 0x21, 0xFF, 0x0B, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B })]
    public static void WriteApplicationExtension_ValidInVersion89a_WritesExpectedBytes(byte[] applicationIdentifier, byte[] applicationAuthenticationCode, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            stream.SetLength(0);

            writer.WriteApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Fact]
    public static void WriteNetscapeApplicationExtensionSubblock_SubblockIsNull_ThrowsArgumentNullException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<ArgumentNullException>(() => writer.WriteNetscapeApplicationExtensionSubblock(null!));

            Assert.Equal("subblock", ex.ParamName);
        }
    }

    [Fact]
    public static void WriteNetscapeApplicationExtensionSubblock_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteNetscapeApplicationExtensionSubblock(new NetscapeApplicationExtension.LoopingSubblock()));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void WriteNetscapeApplicationExtensionSubblock_InvalidExtensionLabel_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            writer.WritePlainTextExtension(default);

            var ex = Assert.Throws<InvalidOperationException>(() => writer.WriteNetscapeApplicationExtensionSubblock(new NetscapeApplicationExtension.LoopingSubblock()));

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Theory]
    [InlineData(0x0000, new byte[] { 0x03, 0x01, 0x00, 0x00 })]
    [InlineData(0x0201, new byte[] { 0x03, 0x01, 0x01, 0x02 })]
    public static void WriteNetscapeApplicationExtensionSubblock_LoopingSubblock_WritesExpectedBytes(ushort loopCount, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            writer.WriteApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            stream.SetLength(0);

            writer.WriteNetscapeApplicationExtensionSubblock(new NetscapeApplicationExtension.LoopingSubblock
            {
                LoopCount = loopCount,
            });

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Theory]
    [InlineData(0x00000000, new byte[] { 0x05, 0x02, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0x04030201, new byte[] { 0x05, 0x02, 0x01, 0x02, 0x03, 0x04 })]
    public static void WriteNetscapeApplicationExtensionSubblock_BufferingSubblock_WritesExpectedBytes(uint bufferLength, byte[] expectedBytes)
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version89a);

            writer.WriteLogicalScreenDescriptor(default);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            writer.WriteApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            stream.SetLength(0);

            writer.WriteNetscapeApplicationExtensionSubblock(new NetscapeApplicationExtension.BufferingSubblock
            {
                BufferLength = bufferLength,
            });

            Assert.Equal(expectedBytes, stream.ToArray());
        }
    }

    [Fact]
    public static void WriteTrailer_InvalidState_ThrowsInvalidOperationException()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(writer.WriteTrailer);

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void WriteTrailer_Valid_WritesExpectedBytes()
    {
        using (var stream = new MemoryStream())
        using (var writer = new GifWriter(stream))
        {
            writer.WriteHeader(GifVersion.Version87a);

            writer.WriteLogicalScreenDescriptor(default);

            stream.SetLength(0);

            writer.WriteTrailer();

            Assert.Equal([0x3B], stream.ToArray());
        }
    }

    private static GifLogicalScreenDescriptor CreateLogicalScreenDescriptor(LogicalScreenDescriptorFields fields)
    {
        return new GifLogicalScreenDescriptor
        {
            Width = fields.Width,
            Height = fields.Height,
            HasGlobalColorTable = fields.HasGlobalColorTable,
            ColorResolution = fields.ColorResolution,
            Sorted = fields.Sorted,
            GlobalColorTableSize = fields.GlobalColorTableSize,
            BackgroundColorIndex = fields.BackgroundColorIndex,
            PixelAspectRatio = fields.PixelAspectRatio,
        };
    }

    private static GifImageDescriptor CreateImageDescriptor(ImageDescriptorFields fields)
    {
        var descriptor = new GifImageDescriptor
        {
            Left = fields.Left,
            Top = fields.Top,
            Width = fields.Width,
            Height = fields.Height,
            HasLocalColorTable = fields.HasLocalColorTable,
            Interlaced = fields.Interlaced,
            Sorted = fields.Sorted,
            LocalColorTableSize = fields.LocalColorTableSize,
        };
        byte packedFields = GifImageDescriptorGetPackedFields(in descriptor);
        GifImageDescriptorSetPackedFields(ref descriptor, (byte)(packedFields | ((fields.Reserved & 0b11) << 3)));
        return descriptor;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_PackedFields")]
    private static extern byte GifImageDescriptorGetPackedFields(ref readonly GifImageDescriptor @this);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_PackedFields")]
    private static extern void GifImageDescriptorSetPackedFields(ref GifImageDescriptor @this, byte value);

    private static GifGraphicControlExtension CreateGraphicControlExtension(GraphicControlExtensionFields fields)
    {
        var extension = new GifGraphicControlExtension
        {
            DisposalMethod = fields.DisposalMethod,
            UserInput = fields.UserInput,
            HasTransparentColor = fields.HasTransparentColor,
            DelayTime = fields.DelayTime,
            TransparentColorIndex = fields.TransparentColorIndex,
        };
        byte packedFields = GifGraphicControlExtensionGetPackedFields(in extension);
        GifGraphicControlExtensionSetPackedFields(ref extension, (byte)(packedFields | (fields.Reserved << 5)));
        return extension;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_PackedFields")]
    private static extern byte GifGraphicControlExtensionGetPackedFields(ref readonly GifGraphicControlExtension @this);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "set_PackedFields")]
    private static extern void GifGraphicControlExtensionSetPackedFields(ref GifGraphicControlExtension @this, byte value);

    private static GifPlainTextExtension CreatePlainTextExtension(PlainTextExtensionFields fields)
    {
        return new GifPlainTextExtension
        {
            Left = fields.Left,
            Top = fields.Top,
            Width = fields.Width,
            Height = fields.Height,
            CellWidth = fields.CellWidth,
            CellHeight = fields.CellHeight,
            ForegroundColorIndex = fields.ForegroundColorIndex,
            BackgroundColorIndex = fields.BackgroundColorIndex,
        };
    }

    private sealed class UnwritableStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => throw new NotImplementedException();

        public override long Position
        {
            get => throw new NotImplementedException();
            set => throw new NotImplementedException();
        }

        public override void Flush()
        {
            throw new NotImplementedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotImplementedException();
        }

        public override void SetLength(long value)
        {
            throw new NotImplementedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }
    }
}
