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
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Xunit;
using GraphicControlExtensionFields = (Tetractic.Formats.Gif.GifDisposalMethod DisposalMethod, bool UserInput, bool HasTransparentColor, ushort DelayTime, byte TransparentColorIndex, byte Reserved);
using ImageDescriptorFields = (ushort Left, ushort Top, ushort Width, ushort Height, bool HasLocalColorTable, bool Interlaced, bool Sorted, byte LocalColorTableSize, byte Reserved);
using LogicalScreenDescriptorFields = (ushort Width, ushort Height, bool HasGlobalColorTable, byte ColorResolution, bool Sorted, byte GlobalColorTableSize, byte BackgroundColorIndex, byte PixelAspectRatio);
using PlainTextExtensionFields = (ushort Left, ushort Top, ushort Width, ushort Height, byte CellWidth, byte CellHeight, byte ForegroundColorIndex, byte BackgroundColorIndex);

namespace Tetractic.Formats.Gif.Tests;

public static class GifReaderTests
{
    [Fact]
    public static void Constructor_StreamIsNull_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new GifReader(null!));

        Assert.Equal("stream", ex.ParamName);
    }

    [Fact]
    public static void Constructor_StreamCannotRead_ThrowsArgumentException()
    {
        using (var stream = new UnreadableStream())
        {
            var ex = Assert.Throws<ArgumentException>(() => new GifReader(stream));

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
            new GifReader(stream, leaveOpen).Dispose();

            Assert.Equal(leaveOpen, stream.CanRead);
        }
    }

    [Fact]
    public static void Peek_Initial_ReturnsHeader()
    {
        byte[] bytes = [];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.Header, part);
        }
    }

    [Fact]
    public static void Peek_AfterReadHeader_ReturnsLogicalScreenDescriptor()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.LogicalScreenDescriptor, part);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x21 }, GifReader.ReadPart.ExtensionLabel)]
    [InlineData(new byte[] { 0x2C }, GifReader.ReadPart.ImageDescriptor)]
    [InlineData(new byte[] { 0x3B }, GifReader.ReadPart.Trailer)]
    public static void Peek_AfterReadLogicalScreenDescriptorWithoutGlobalColorTable_ReturnsExpectedResult(byte[] blockBytes, GifReader.ReadPart expectedBlockType)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();

            Assert.Equal(expectedBlockType, part);
        }
    }

    [Fact]
    public static void Peek_AfterReadLogicalScreenDescriptorWithGlobalColorTable_ReturnsGlobalColorTable()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.GlobalColorTable, part);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x21 }, GifReader.ReadPart.ExtensionLabel)]
    [InlineData(new byte[] { 0x2C }, GifReader.ReadPart.ImageDescriptor)]
    [InlineData(new byte[] { 0x3B }, GifReader.ReadPart.Trailer)]
    public static void Peek_AfterReadGlobalColorTable_ReturnsExpectedResult(byte[] blockBytes, GifReader.ReadPart expectedBlockType)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadColorTable();

            var part = reader.Peek();

            Assert.Equal(expectedBlockType, part);
        }
    }

    [Fact]
    public static void Peek_AfterReadImageDescriptorWithoutLocalColorTable_ReturnsImageData()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.ImageData, part);
        }
    }

    [Fact]
    public static void Peek_AfterReadImageDescriptorWithLocalColorTable_ReturnsLocalColorTable()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80,
            0x00, 0x00, 0x00,
            0x00, 0x00, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.LocalColorTable, part);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x21 }, GifReader.ReadPart.ExtensionLabel)]
    [InlineData(new byte[] { 0x2C }, GifReader.ReadPart.ImageDescriptor)]
    [InlineData(new byte[] { 0x3B }, GifReader.ReadPart.Trailer)]
    public static void Peek_AfterReadImageData_ReturnsExpectedResult(byte[] blockBytes, GifReader.ReadPart expectedBlockType)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x07,
            0x01, 0x81,
            0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();
            _ = reader.ReadImageData();

            var part = reader.Peek();

            Assert.Equal(expectedBlockType, part);
        }
    }

    [Fact]
    public static void Peek_AfterReadSubblock_ReturnsSubblock()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFE,
            0x01, 0x00,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            byte[]? subblock = reader.ReadSubblock();

            Debug.Assert(subblock is not null);

            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.Subblock, part);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x21 }, GifReader.ReadPart.ExtensionLabel)]
    [InlineData(new byte[] { 0x2C }, GifReader.ReadPart.ImageDescriptor)]
    [InlineData(new byte[] { 0x3B }, GifReader.ReadPart.Trailer)]
    public static void Peek_AfterReadSubblockReturnsNull_ReturnsExpectedResult(byte[] blockBytes, GifReader.ReadPart expectedBlockType)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFE,
            0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            byte[]? subblock = reader.ReadSubblock();

            Debug.Assert(subblock is null);

            var part = reader.Peek();

            Assert.Equal(expectedBlockType, part);
        }
    }

    [Fact]
    public static void Peek_AfterReadGraphicControlExtension_ReturnsSubblock()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xF9, 0x04, 0x00, 0x00, 0x00, 0x00,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            _ = reader.ReadGraphicControlExtension();

            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.Subblock, part);
        }
    }

    [Fact]
    public static void Peek_AfterReadPlainTextExtension_ReturnsSubblock()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0x01,
            0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            _ = reader.ReadPlainTextExtension();

            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.Subblock, part);
        }
    }

    [Fact]
    public static void Peek_AfterReadApplicationExtension_ReturnsSubblock()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x01, 0x00,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.Subblock, part);
        }
    }

    [Fact]
    public static void Peek_AfterReadNetscapeApplicationExtensionSubblock_ReturnsSubblock()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x03, 0x01, 0x00, 0x00,
            0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            var subblock = reader.ReadNetscapeApplicationExtensionSubblock();

            Debug.Assert(subblock is not null);

            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.Subblock, part);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x21 }, GifReader.ReadPart.ExtensionLabel)]
    [InlineData(new byte[] { 0x2C }, GifReader.ReadPart.ImageDescriptor)]
    [InlineData(new byte[] { 0x3B }, GifReader.ReadPart.Trailer)]
    public static void Peek_AfterReadNetscapeApplicationExtensionSubblockReturnsNull_ReturnsExpectedResult(byte[] blockBytes, GifReader.ReadPart expectedBlockType)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            var subblock = reader.ReadNetscapeApplicationExtensionSubblock();

            Debug.Assert(subblock is null);

            var part = reader.Peek();

            Assert.Equal(expectedBlockType, part);
        }
    }

    [Fact]
    public static void Peek_AfterReadExtensionLabel_ReturnsSubblock()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();

            var part = reader.Peek();

            Assert.Equal(GifReader.ReadPart.Subblock, part);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x21 }, GifReader.ReadPart.ExtensionLabel)]
    [InlineData(new byte[] { 0x2C }, GifReader.ReadPart.ImageDescriptor)]
    [InlineData(new byte[] { 0x3B }, GifReader.ReadPart.Trailer)]
    public static void Peek_Again_ReturnsSameResult(byte[] blockBytes, GifReader.ReadPart expectedBlockType)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();

            var part = reader.Peek();

            Assert.Equal(expectedBlockType, part);
        }
    }

    [Fact]
    public static void Peek_InvalidBlockType_ThrowsInvalidDataException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var ex = Assert.Throws<InvalidDataException>(() => reader.Peek());

            Assert.Equal("An unrecognized block label was read.", ex.Message);
        }
    }

    [Fact]
    public static void Peek_EndOfFile_ThrowsEndOfStreamException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var ex = Assert.Throws<EndOfStreamException>(() => reader.Peek());

            AssertIsErrorState(reader);
        }
    }

    [Fact]
    public static void ReadHeader_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadHeader());

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 })]
    public static void ReadHeader_EndOfFile_ThrowsEndOfStreamException(byte[] bytes)
    {
        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<EndOfStreamException>(() => reader.ReadHeader());

            AssertIsErrorState(reader);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x67, 0x69, 0x66, 0x38, 0x37, 0x61 })]  // "gif87a"
    public static void ReadHeader_InvalidSignature_ThrowsInvalidDataException(byte[] bytes)
    {
        Debug.WriteLine(Encoding.ASCII.GetString(bytes));

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidDataException>(() => reader.ReadHeader());

            Assert.Equal("Invalid format signature.", ex.Message);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x2F, 0x37, 0x61 })]  // "GIF/7a"
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x3A, 0x37, 0x61 })]  // "GIF:7a"
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x2F, 0x61 })]  // "GIF8/a"
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x3A, 0x61 })]  // "GIF8:a"
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x60 })]  // "GIF87`"
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x7B })]  // "GIF87{"
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x41 })]  // "GIF87A"
    public static void ReadHeader_InvalidVersion_ThrowsInvalidDataException(byte[] bytes)
    {
        Debug.WriteLine(Encoding.ASCII.GetString(bytes));

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidDataException>(() => reader.ReadHeader());

            Assert.Equal("Invalid format version.", ex.Message);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x37, 0x61 }, GifVersion.Version87a)]              // "GIF87a"
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x39, 0x61 }, GifVersion.Version89a)]              // "GIF89a"
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x39, 0x39, 0x7A }, (GifVersion)((99 - 87) * 26 + 25))]  // "GIF99z"
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x30, 0x30, 0x61 }, (GifVersion)((100 - 87) * 26))]      // "GIF00a"
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38, 0x36, 0x7A }, (GifVersion)(100 * 26 - 1))]         // "GIF86z"
    public static void ReadHeader_Valid_ReturnsExpectedResult(byte[] bytes, GifVersion expectedVersion)
    {
        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var version = reader.ReadHeader();

            Assert.Equal(expectedVersion, version);
        }
    }

    [Fact]
    public static void ReadLogicalScreenDescriptor_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadLogicalScreenDescriptor());

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    public static void ReadLogicalScreenDescriptor_EndOfFile_ThrowsEndOfStreamException(byte[] blockBytes)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            var ex = Assert.Throws<EndOfStreamException>(() => reader.ReadLogicalScreenDescriptor());

            AssertIsErrorState(reader);
        }
    }

    public static readonly TheoryData<byte[], LogicalScreenDescriptorFields, string> ReadLogicalScreenDescriptor_ValidInVersion87a_Data = new()
    {
        { new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, (0, 0, false, 0, false, 0, 0, 0), null! },
        { new byte[] { 0x01, 0x02, 0x03, 0x04, 0x00, 0x05, 0x00 }, (0x0201, 0x0403, false, 0, false, 0, 0x05, 0x00), null! },
        { new byte[] { 0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00 }, (0, 0, true, 0, false, 0, 0, 0), null! },
        { new byte[] { 0x00, 0x00, 0x00, 0x00, 0x70, 0x00, 0x00 }, (0, 0, false, 7, false, 0, 0, 0), null! },
        { new byte[] { 0x00, 0x00, 0x00, 0x00, 0x07, 0x00, 0x00 }, (0, 0, false, 0, false, 7, 0, 0), null! },
    };

    public static readonly TheoryData<byte[], LogicalScreenDescriptorFields, string> ReadLogicalScreenDescriptor_ValidInVersion89a_Data = new()
    {
        { new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }, (0, 0, false, 0, false, 0, 0, 1), "Pixel aspect ratio is not valid for format version." },
        { new byte[] { 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00 }, (0, 0, false, 0, true, 0, 0, 0), "Sorted color table is not valid for format version." },
    };

    [Theory]
    [MemberData(nameof(ReadLogicalScreenDescriptor_ValidInVersion89a_Data))]
    public static void ReadLogicalScreenDescriptor_InvalidInVersion87a_ThrowsInvalidDataException(byte[] blockBytes, LogicalScreenDescriptorFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            var ex = Assert.Throws<InvalidDataException>(() => reader.ReadLogicalScreenDescriptor());

            Assert.Equal(expectedMessage, ex.Message);
        }

        _ = expectedFields;
    }

    [Theory]
    [MemberData(nameof(ReadLogicalScreenDescriptor_ValidInVersion87a_Data))]
    public static void ReadLogicalScreenDescriptor_ValidInVersion87a_ReturnsExpectedResults(byte[] blockBytes, LogicalScreenDescriptorFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            .. blockBytes
        ];

        ReadLogicalScreenDescriptor_ValidInVersion_ReturnsExpectedResults(bytes, expectedFields);

        _ = expectedMessage;
    }

    [Theory]
    [MemberData(nameof(ReadLogicalScreenDescriptor_ValidInVersion87a_Data))]
    [MemberData(nameof(ReadLogicalScreenDescriptor_ValidInVersion89a_Data))]
    public static void ReadLogicalScreenDescriptor_ValidInVersion89a_ReturnsExpectedResults(byte[] blockBytes, LogicalScreenDescriptorFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            .. blockBytes
        ];

        ReadLogicalScreenDescriptor_ValidInVersion_ReturnsExpectedResults(bytes, expectedFields);

        _ = expectedMessage;
    }

    private static void ReadLogicalScreenDescriptor_ValidInVersion_ReturnsExpectedResults(byte[] bytes, LogicalScreenDescriptorFields expectedFields)
    {
        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            var descriptor = reader.ReadLogicalScreenDescriptor();

            Assert.Equal(expectedFields.Width, descriptor.Width);
            Assert.Equal(expectedFields.Height, descriptor.Height);
            Assert.Equal(expectedFields.HasGlobalColorTable, descriptor.HasGlobalColorTable);
            Assert.Equal(expectedFields.ColorResolution, descriptor.ColorResolution);
            Assert.Equal(expectedFields.Sorted, descriptor.Sorted);
            Assert.Equal(expectedFields.GlobalColorTableSize, descriptor.GlobalColorTableSize);
            Assert.Equal(expectedFields.BackgroundColorIndex, descriptor.BackgroundColorIndex);
            Assert.Equal(expectedFields.PixelAspectRatio, descriptor.PixelAspectRatio);
        }
    }

    [Fact]
    public static void ReadColorTable_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(reader.ReadColorTable);

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadColorTable_EndOfFile_ThrowsEndOfStreamException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00,
            0x00, 0x00, 0x00,
            0x00, 0x00,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var ex = Assert.Throws<EndOfStreamException>(reader.ReadColorTable);

            AssertIsErrorState(reader);
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(7, 256)]
    public static void ReadColorTable_GlobalColorTable_ReturnsExpectedResult(int colorTableSize, int expectedColorCount)
    {
        byte[] colorBytes = new byte[expectedColorCount * 3];
        for (int i = 0; i < colorBytes.Length; ++i)
            colorBytes[i] = (byte)i;

        var expectedColors = new GifColor[expectedColorCount];
        for (int i = 0, j = 0; i < expectedColors.Length; ++i, j += 3)
            expectedColors[i] = new GifColor((byte)j, (byte)(j + 1), (byte)(j + 2));

        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, (byte)(0x80 | colorTableSize), 0x00, 0x00,
            ..colorBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var colors = reader.ReadColorTable();

            Assert.Equal(expectedColors, colors);
        }
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(7, 256)]
    public static void ReadColorTable_LocalColorTable_ReturnsExpectedResult(int colorTableSize, int expectedColorCount)
    {
        byte[] colorBytes = new byte[expectedColorCount * 3];
        for (int i = 0; i < colorBytes.Length; ++i)
            colorBytes[i] = (byte)i;

        var expectedColors = new GifColor[expectedColorCount];
        for (int i = 0, j = 0; i < expectedColors.Length; ++i, j += 3)
            expectedColors[i] = new GifColor((byte)j, (byte)(j + 1), (byte)(j + 2));

        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, (byte)(0x80 | colorTableSize),
            ..colorBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var colors = reader.ReadColorTable();

            Assert.Equal(expectedColors, colors);
        }
    }

    [Fact]
    public static void ReadImageDescriptor_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadImageDescriptor());

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadImageDescriptor_EndOfFile_ThrowsEndOfStreamException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();

            var ex = Assert.Throws<EndOfStreamException>(() => reader.ReadImageDescriptor());

            AssertIsErrorState(reader);
        }
    }

    public static readonly TheoryData<byte[], ImageDescriptorFields, string> ReadImageDescriptor_ValidInVersion87a_Data = new()
    {
        { new byte[] { 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, (0, 0, 0, 0, false, false, false, 0, 0), null! },
        { new byte[] { 0x2C, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x00 }, (0x0201, 0x0403, 0x0605, 0x0807, false, false, false, 0, 0), null! },
        { new byte[] { 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x80 }, (0, 0, 0, 0, true, false, false, 0, 0), null! },
        { new byte[] { 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40 }, (0, 0, 0, 0, false, true, false, 0, 0), null! },
        { new byte[] { 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x04 }, (0, 0, 0, 0, false, false, false, 4, 0), null! },
        { new byte[] { 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x02 }, (0, 0, 0, 0, false, false, false, 2, 0), null! },
        { new byte[] { 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 }, (0, 0, 0, 0, false, false, false, 1, 0), null! },
    };

    public static readonly TheoryData<byte[], ImageDescriptorFields, string> ReadImageDescriptor_ValidInVersion89a_Data = new()
    {
        { new byte[] { 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x20 }, (0, 0, 0, 0, false, false, true, 0, 0), "Sorted color table is not valid for format version." },
    };

    public static readonly TheoryData<byte[], ImageDescriptorFields, string> ReadImageDescriptor_ValidInUnknownVersion_Data = new()
    {
        { new byte[] { 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0b0_0_0_10_000 }, (0, 0, 0, 0, false, false, false, 0, 0b10), "Reserved bits are set." },
        { new byte[] { 0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0b0_0_0_01_000 }, (0, 0, 0, 0, false, false, false, 0, 0b01), "Reserved bits are set." },
    };

    [Theory]
    [MemberData(nameof(ReadImageDescriptor_ValidInVersion89a_Data))]
    [MemberData(nameof(ReadImageDescriptor_ValidInUnknownVersion_Data))]
    public static void ReadImageDescriptor_InvalidInVersion87a_ThrowsInvalidDataException(byte[] blockBytes, ImageDescriptorFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        ReadImageDescriptor_InvalidInVersion_ThrowsInvalidDataException(bytes, expectedFields, expectedMessage);
    }

    [Theory]
    [MemberData(nameof(ReadImageDescriptor_ValidInUnknownVersion_Data))]
    public static void ReadImageDescriptor_InvalidInVersion89a_ThrowsInvalidDataException(byte[] blockBytes, ImageDescriptorFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        ReadImageDescriptor_InvalidInVersion_ThrowsInvalidDataException(bytes, expectedFields, expectedMessage);
    }

    private static void ReadImageDescriptor_InvalidInVersion_ThrowsInvalidDataException(byte[] bytes, ImageDescriptorFields expectedFields, string expectedMessage)
    {
        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();

            var ex = Assert.Throws<InvalidDataException>(() => reader.ReadImageDescriptor());

            Assert.Equal(expectedMessage, ex.Message);
        }

        _ = expectedFields;
    }

    [Theory]
    [MemberData(nameof(ReadImageDescriptor_ValidInVersion87a_Data))]
    public static void ReadImageDescriptor_ValidInVersion87a_ReturnsExpectedResults(byte[] blockBytes, ImageDescriptorFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        ReadImageDescriptor_ValidInVersion_ReturnsExpectedResults(bytes, expectedFields);

        _ = expectedMessage;
    }

    [Theory]
    [MemberData(nameof(ReadImageDescriptor_ValidInVersion87a_Data))]
    [MemberData(nameof(ReadImageDescriptor_ValidInVersion89a_Data))]
    public static void ReadImageDescriptor_ValidInVersion89a_ReturnsExpectedResults(byte[] blockBytes, ImageDescriptorFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        ReadImageDescriptor_ValidInVersion_ReturnsExpectedResults(bytes, expectedFields);

        _ = expectedMessage;
    }

    [Theory]
    [MemberData(nameof(ReadImageDescriptor_ValidInVersion87a_Data))]
    [MemberData(nameof(ReadImageDescriptor_ValidInVersion89a_Data))]
    [MemberData(nameof(ReadImageDescriptor_ValidInUnknownVersion_Data))]
    public static void ReadImageDescriptor_ValidInUnknownVersion_ReturnsExpectedResults(byte[] blockBytes, ImageDescriptorFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF89b"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        ReadImageDescriptor_ValidInVersion_ReturnsExpectedResults(bytes, expectedFields);

        _ = expectedMessage;
    }

    private static void ReadImageDescriptor_ValidInVersion_ReturnsExpectedResults(byte[] bytes, ImageDescriptorFields expectedFields)
    {
        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();

            var descriptor = reader.ReadImageDescriptor();

            Assert.Equal(expectedFields.Left, descriptor.Left);
            Assert.Equal(expectedFields.Top, descriptor.Top);
            Assert.Equal(expectedFields.Width, descriptor.Width);
            Assert.Equal(expectedFields.Height, descriptor.Height);
            Assert.Equal(expectedFields.HasLocalColorTable, descriptor.HasLocalColorTable);
            Assert.Equal(expectedFields.Interlaced, descriptor.Interlaced);
            Assert.Equal(expectedFields.Sorted, descriptor.Sorted);
            Assert.Equal(expectedFields.LocalColorTableSize, descriptor.LocalColorTableSize);
        }
    }

    [Fact]
    public static void ReadImageData_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(reader.ReadImageData);

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadImageData_EndOfFile_ThrowsEndOfStreamException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var ex = Assert.Throws<EndOfStreamException>(reader.ReadImageData);

            AssertIsErrorState(reader);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x00 })]
    [InlineData(new byte[] { 0x01 })]
    [InlineData(new byte[] { 0x09 })]
    public static void ReadImageData_InvalidCodeSize_ThrowsInvalidDataException(byte[] blockBytes)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var ex = Assert.Throws<InvalidDataException>(reader.ReadImageData);

            Assert.Equal("Invalid LZW code size.", ex.Message);
        }
    }

    [Fact]
    public static void ReadImageData_Truncated_ThrowsInvalidDataException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x07,
            0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var ex = Assert.Throws<InvalidDataException>(reader.ReadImageData);

            Assert.Equal("LZW code stream is truncated.", ex.Message);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x07, 0x02, 0x81, 0x00 })]
    [InlineData(new byte[] { 0x07, 0x01, 0x81, 0x01, 0x00 })]
    public static void ReadImageData_TrailingGarbage_ThrowsInvalidDataException(byte[] imageDataBytes)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. imageDataBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var ex = Assert.Throws<InvalidDataException>(reader.ReadImageData);

            Assert.Equal("LZW code stream has trailing garbage.", ex.Message);
        }
    }

    [Fact]
    public static void ReadImageData_TooLittleData_ThrowsInvalidDataException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0x07,
            0x01, 0x81,
            0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var ex = Assert.Throws<InvalidDataException>(reader.ReadImageData);

            Assert.Equal("LZW code stream produced too little data.", ex.Message);
        }
    }

    [Theory]
    [InlineData(1, 1, new byte[] { 0x07, 0x01, 0x82 })]
    [InlineData(2, 2, new byte[] { 0x07, 0x03, 0x02, 0x080, 0x82 })]
    public static void ReadImageData_InvalidCode_ThrowsInvalidDataException(ushort width, ushort height, byte[] blockBytes)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var ex = Assert.Throws<InvalidDataException>(reader.ReadImageData);

            Assert.Equal("Invalid LZW code.", ex.Message);
        }
    }

    [Fact]
    public static void ReadImageData_TooMuchData_ThrowsInvalidDataException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x07,
            0x01, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var ex = Assert.Throws<InvalidDataException>(reader.ReadImageData);

            Assert.Equal("LZW code stream produced too much data.", ex.Message);
        }
    }

    [Fact]
    public static void ReadImageData_TooMuchDataFreeCode_ThrowsInvalidDataException()
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, 0x03, 0x00, 0x01, 0x00, 0x00,
            0x07,
            0x03, 0x00, 0x00, 0x83
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            var ex = Assert.Throws<InvalidDataException>(reader.ReadImageData);

            Assert.Equal("LZW code stream produced too much data.", ex.Message);
        }
    }

    public static IEnumerable<TheoryDataRow<ushort, ushort, byte[], byte[]>> ReadImageData_Valid_CodeExhaustionData()
    {
        var builder = new ImageDataBlocksBuilder(8);

        var dictionary = new Dictionary<int, byte[]>();
        for (int i = 0; i < 256; ++i)
            dictionary[i] = [(byte)i];
        dictionary[dictionary.Count] = null!;
        dictionary[dictionary.Count] = null!;

        byte[]? previousBytes = null;

        var imageData = new List<byte>();

        for (int j = 0; j < 2; ++j)
        {
            for (int i = 0; i < 256; ++i)
                AddCode(i);

            for (int i = 258; i < 4096; ++i)
                AddCode(i);
        }

        builder.AddCode(257);

        yield return (1087, 64, [.. builder.MoveToBlockBytes()], [.. imageData]);

        void AddCode(int code)
        {
            builder.AddCode(code);

            byte[] bytes = dictionary[code];

            imageData.AddRange(bytes);

            if (previousBytes is not null && dictionary.Count < 4096)
                dictionary[dictionary.Count] = [.. previousBytes, bytes[0]];

            previousBytes = bytes;
        }
    }

    [Theory]
    [InlineData(0, 0, new byte[] { 0x03, 0x01, 0x09, 0x00 }, new byte[0])]  // End code
    [InlineData(0, 0, new byte[] { 0x07, 0x01, 0x81, 0x00 }, new byte[0])]  // End code
    [InlineData(1, 1, new byte[] { 0x07, 0x02, 0x01, 0x81, 0x00 }, new byte[] { 0x01 })]  // Raw code
    [InlineData(4, 1, new byte[] { 0x07, 0x04, 0x01, 0x02, 0x82, 0x81, 0x00 }, new byte[] { 0x01, 0x02, 0x01, 0x02 })]  // Dictionary code
    [InlineData(2, 2, new byte[] { 0x07, 0x04, 0x01, 0x02, 0x83, 0x81, 0x00 }, new byte[] { 0x01, 0x02, 0x02, 0x02 })]  // Free code after raw code
    [InlineData(1, 7, new byte[] { 0x07, 0x05, 0x01, 0x02, 0x82, 0x84, 0x81, 0x00 }, new byte[] { 0x01, 0x02, 0x01, 0x02, 0x01, 0x02, 0x01 })]  // Free code after dictionary code
    [InlineData(3, 2, new byte[] { 0x07, 0x07, 0x01, 0x02, 0x80, 0x03, 0x04, 0x82, 0x81, 0x00 }, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x03, 0x04 })]  // Clear code resets dictionary
    [InlineData(2, 3, new byte[] { 0x02, 0x04, 0x88, 0x86, 0x94, 0x02, 0x00 }, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x02, 0x01 })]  // Clear code resets code size
    [MemberData(nameof(ReadImageData_Valid_CodeExhaustionData))]
    public static void ReadImageData_Valid_ReturnsExpectedResult(ushort width, ushort height, byte[] blockBytes, byte[] expectedImageData)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x2C, 0x00, 0x00, 0x00, 0x00, (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8), 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();

            byte[] imageData = reader.ReadImageData();

            Assert.Equal(expectedImageData, imageData);
        }
    }

    [Fact]
    public static void ReadExtensionLabel_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes = [];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadExtensionLabel());

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadExtensionLabel_EndOfFile_ThrowEndOfFileException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();

            var ex = Assert.Throws<EndOfStreamException>(() => reader.ReadExtensionLabel());

            AssertIsErrorState(reader);
        }
    }

    public static readonly TheoryData<byte, GifExtensionLabel> ReadExtensionLabel_ValidInVersion89a_Data = new()
    {
        { (byte)GifExtensionLabel.PlainText, GifExtensionLabel.PlainText },
        { (byte)GifExtensionLabel.GraphicControl, GifExtensionLabel.GraphicControl },
        { (byte)GifExtensionLabel.Comment, GifExtensionLabel.Comment },
        { (byte)GifExtensionLabel.Application, GifExtensionLabel.Application },
    };

    public static readonly TheoryData<byte, GifExtensionLabel> ReadExtensionLabel_ValidInUnknownVersion_Data = new()
    {
        { 0x00, 0x00 },
    };

    [Theory]
    [MemberData(nameof(ReadExtensionLabel_ValidInVersion89a_Data))]
    [MemberData(nameof(ReadExtensionLabel_ValidInUnknownVersion_Data))]
    public static void ReadExtensionLabel_InvalidInVersion87a_ThrowsInvalidDataException(byte labelByte, GifExtensionLabel expectedLabel)
    {
        byte[] bytes =
        [
            .. "GIF87a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, labelByte
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();

            var ex = Assert.Throws<InvalidDataException>(() => reader.ReadExtensionLabel());

            Assert.Equal("Extension is not valid for format version.", ex.Message);
        }

        _ = expectedLabel;
    }

    [Theory]
    [MemberData(nameof(ReadExtensionLabel_ValidInUnknownVersion_Data))]
    public static void ReadExtensionLabel_InvalidInVersion89a_ThrowsInvalidDataException(byte labelByte, GifExtensionLabel expectedLabel)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, labelByte
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();

            var ex = Assert.Throws<InvalidDataException>(() => reader.ReadExtensionLabel());

            Assert.Equal("Extension is not valid for format version.", ex.Message);
        }

        _ = expectedLabel;
    }

    [Theory]
    [MemberData(nameof(ReadExtensionLabel_ValidInVersion89a_Data))]
    public static void ReadExtensionLabel_ValidInVersion89a_ReturnsExpectedResult(byte labelByte, GifExtensionLabel expectedLabel)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, labelByte
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();

            var label = reader.ReadExtensionLabel();

            Assert.Equal(expectedLabel, label);
        }
    }

    [Theory]
    [MemberData(nameof(ReadExtensionLabel_ValidInVersion89a_Data))]
    [MemberData(nameof(ReadExtensionLabel_ValidInUnknownVersion_Data))]
    public static void ReadExtensionLabel_ValidInUnknownVersion_ReturnsExpectedResult(byte labelByte, GifExtensionLabel expectedLabel)
    {
        byte[] bytes =
        [
            .. "GIF89b"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, labelByte
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();

            var label = reader.ReadExtensionLabel();

            Assert.Equal(expectedLabel, label);
        }
    }

    [Fact]
    public static void ReadSubblock_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(reader.ReadSubblock);

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadSubblock_EndOfFile_ThrowsEndOfStreamException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFE,
            0x01
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Comment);

            var ex = Assert.Throws<EndOfStreamException>(reader.ReadSubblock);

            AssertIsErrorState(reader);
        }
    }

    private static readonly IEnumerable<byte> _255bytes = Enumerable.Range(0, 255).Select(x => (byte)x);

    public static readonly TheoryData<byte[], byte[]> ReadSubblock_Data = new()
    {
        { new byte[] { 0x01, 0x00 }, new byte[] { 0x00 } },
        { new byte[] { 0x02, 0x01, 0x02 }, new byte[] { 0x01, 0x02 } },
        { [0xFF, .. _255bytes], [.. _255bytes] },
    };

    [Theory]
    [MemberData(nameof(ReadSubblock_Data))]
    public static void ReadSubblock_PlainTextExtension_ReturnsExpectedResult(byte[] blockBytes, byte[] expectedResult)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0x01,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.PlainText);

            byte[]? result = reader.ReadSubblock();

            Assert.Equal(expectedResult, result);
        }
    }

    [Theory]
    [MemberData(nameof(ReadSubblock_Data))]
    public static void ReadSubblock_AfterReadPlainTextExtension_ReturnsExpectedResult(byte[] blockBytes, byte[] expectedResult)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0x01,
            0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.PlainText);

            _ = reader.ReadPlainTextExtension();

            byte[]? result = reader.ReadSubblock();

            Assert.Equal(expectedResult, result);
        }
    }

    [Theory]
    [MemberData(nameof(ReadSubblock_Data))]
    public static void ReadSubblock_GraphicControlExtension_ReturnsExpectedResult(byte[] blockBytes, byte[] expectedResult)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xF9,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.GraphicControl);

            byte[]? result = reader.ReadSubblock();

            Assert.Equal(expectedResult, result);
        }
    }

    [Theory]
    [MemberData(nameof(ReadSubblock_Data))]
    public static void ReadSubblock_AfterReadGraphicControlExtension_ReturnsExpectedResult(byte[] blockBytes, byte[] expectedResult)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xF9,
            0x04, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.GraphicControl);

            _ = reader.ReadGraphicControlExtension();

            byte[]? result = reader.ReadSubblock();

            Assert.Equal(expectedResult, result);
        }
    }

    [Theory]
    [MemberData(nameof(ReadSubblock_Data))]
    public static void ReadSubblock_CommentExtension_ReturnsExpectedResult(byte[] blockBytes, byte[] expectedResult)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFE,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Comment);

            byte[]? result = reader.ReadSubblock();

            Assert.Equal(expectedResult, result);
        }
    }

    [Theory]
    [MemberData(nameof(ReadSubblock_Data))]
    public static void ReadSubblock_AfterCommentExtension_ReturnsExpectedResult(byte[] blockBytes, byte[] expectedResult)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFE,
            0x01, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Comment);

            _ = reader.ReadSubblock();

            byte[]? result = reader.ReadSubblock();

            Assert.Equal(expectedResult, result);
        }
    }

    [Theory]
    [MemberData(nameof(ReadSubblock_Data))]
    public static void ReadSubblock_ReadApplicationExtension_ReturnsExpectedResult(byte[] blockBytes, byte[] expectedResult)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            byte[]? result = reader.ReadSubblock();

            Assert.Equal(expectedResult, result);
        }
    }

    [Theory]
    [MemberData(nameof(ReadSubblock_Data))]
    public static void ReadSubblock_AfterReadApplicationExtension_ReturnsExpectedResult(byte[] blockBytes, byte[] expectedResult)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            byte[]? result = reader.ReadSubblock();

            Assert.Equal(expectedResult, result);
        }
    }

    [Theory]
    [MemberData(nameof(ReadSubblock_Data))]
    public static void ReadSubblock_UnknownExtension_ReturnsExpectedResult(byte[] blockBytes, byte[] expectedResult)
    {
        byte[] bytes =
        [
            .. "GIF89b"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == 0x00);

            byte[]? result = reader.ReadSubblock();

            Assert.Equal(expectedResult, result);
        }
    }

    [Fact]
    public static void ReadSubblock_BlockTerminator_ReturnsExpectedResult()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFE,
            0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Comment);

            byte[]? result = reader.ReadSubblock();

            Assert.Null(result);
        }
    }

    [Fact]
    public static void ReadGraphicControlExtension_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadGraphicControlExtension());

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadGraphicControlExtension_InvalidBlockType_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x3B,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.Trailer);

            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadGraphicControlExtension());

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadGraphicControlExtension_InvalidExtensionLabel_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0x01,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.PlainText);

            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadGraphicControlExtension());

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadGraphicControlExtension_EndOfFile_ThrowsEndOfStreamException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xF9,
            0x04, 0x00, 0x00, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.GraphicControl);

            var ex = Assert.Throws<EndOfStreamException>(() => reader.ReadGraphicControlExtension());

            AssertIsErrorState(reader);
        }
    }

    [Fact]
    public static void ReadGraphicControlExtension_InvalidBlockSize_ThrowsInvalidDataException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xF9,
            0x03, 0x00, 0x00, 0x00, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.GraphicControl);

            var ex = Assert.Throws<InvalidDataException>(() => reader.ReadGraphicControlExtension());

            Assert.Equal("Invalid block size.", ex.Message);
        }
    }

    public static readonly TheoryData<byte[], GraphicControlExtensionFields, string> ReadGraphicControlExtension_ValidInVersion89a_Data = new()
    {
        { new byte[] { 0x21, 0xF9, 0x04, 0x00, 0x00, 0x00, 0x00 }, (GifDisposalMethod.Unspecified, false, false, 0, 0, 0), null! },
        { new byte[] { 0x21, 0xF9, 0x04, 0x04, 0x00, 0x00, 0x00 }, (GifDisposalMethod.DoNotDispose, false, false, 0, 0, 0), null! },
        { new byte[] { 0x21, 0xF9, 0x04, 0x08, 0x00, 0x00, 0x00 }, (GifDisposalMethod.RestoreToBackground, false, false, 0, 0, 0), null! },
        { new byte[] { 0x21, 0xF9, 0x04, 0x0C, 0x00, 0x00, 0x00 }, (GifDisposalMethod.RestoreToPrevious, false, false, 0, 0, 0), null! },
        { new byte[] { 0x21, 0xF9, 0x04, 0x02, 0x00, 0x00, 0x00 }, (GifDisposalMethod.Unspecified, true, false, 0, 0, 0), null! },
        { new byte[] { 0x21, 0xF9, 0x04, 0x01, 0x00, 0x00, 0x00 }, (GifDisposalMethod.Unspecified, false, true, 0, 0, 0), null! },
        { new byte[] { 0x21, 0xF9, 0x04, 0x00, 0x01, 0x02, 0x00 }, (GifDisposalMethod.Unspecified, false, false, 0x0201, 0, 0), null! },
        { new byte[] { 0x21, 0xF9, 0x04, 0x00, 0x00, 0x00, 0x01 }, (GifDisposalMethod.Unspecified, false, false, 0, 0x01, 0), null! },
    };

    public static readonly TheoryData<byte[], GraphicControlExtensionFields, string> ReadGraphicControlExtension_ValidInUnknownVersion_Data = new()
    {
        { new byte[] { 0x21, 0xF9, 0x04, 0b100_000_0_0, 0x00, 0x00, 0x00 }, (GifDisposalMethod.Unspecified, false, false, 0, 0, 0b100), "Reserved bits are set." },
        { new byte[] { 0x21, 0xF9, 0x04, 0b010_000_0_0, 0x00, 0x00, 0x00 }, (GifDisposalMethod.Unspecified, false, false, 0, 0, 0b010), "Reserved bits are set." },
        { new byte[] { 0x21, 0xF9, 0x04, 0b001_000_0_0, 0x00, 0x00, 0x00 }, (GifDisposalMethod.Unspecified, false, false, 0, 0, 0b001), "Reserved bits are set." },
        { new byte[] { 0x21, 0xF9, 0x04, 0b000_100_0_0, 0x00, 0x00, 0x00 }, ((GifDisposalMethod)0b100, false, false, 0, 0, 0), "Disposal method is undefined in format version." },
        { new byte[] { 0x21, 0xF9, 0x04, 0b000_111_0_0, 0x00, 0x00, 0x00 }, ((GifDisposalMethod)0b111, false, false, 0, 0, 0), "Disposal method is undefined in format version." },
    };

    [Theory]
    [MemberData(nameof(ReadGraphicControlExtension_ValidInUnknownVersion_Data))]
    public static void ReadGraphicControlExtension_InvalidInVersion89a_ThrowsInvalidDataException(byte[] blockBytes, GraphicControlExtensionFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();

            var ex = Assert.Throws<InvalidDataException>(() => reader.ReadGraphicControlExtension());

            Assert.Equal(expectedMessage, ex.Message);
        }

        _ = expectedFields;
    }

    [Theory]
    [MemberData(nameof(ReadGraphicControlExtension_ValidInVersion89a_Data))]
    public static void ReadGraphicControlExtension_ValidInVersion89a_ReturnsExpectedResults(byte[] blockBytes, GraphicControlExtensionFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        ReadGraphicControlExtension_ValidInVersion_ReturnsExpectedResults(bytes, expectedFields);

        _ = expectedMessage;
    }

    [Theory]
    [MemberData(nameof(ReadGraphicControlExtension_ValidInVersion89a_Data))]
    [MemberData(nameof(ReadGraphicControlExtension_ValidInUnknownVersion_Data))]
    public static void ReadGraphicControlExtension_ValidInUnknownVersion_ReturnsExpectedResults(byte[] blockBytes, GraphicControlExtensionFields expectedFields, string expectedMessage)
    {
        byte[] bytes =
        [
            .. "GIF89b"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        ReadGraphicControlExtension_ValidInVersion_ReturnsExpectedResults(bytes, expectedFields);

        _ = expectedMessage;
    }

    private static void ReadGraphicControlExtension_ValidInVersion_ReturnsExpectedResults(byte[] bytes, GraphicControlExtensionFields expectedFields)
    {
        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            var extension = reader.ReadGraphicControlExtension();

            Assert.Equal(expectedFields.DisposalMethod, extension.DisposalMethod);
            Assert.Equal(expectedFields.UserInput, extension.UserInput);
            Assert.Equal(expectedFields.HasTransparentColor, extension.HasTransparentColor);
            Assert.Equal(expectedFields.DelayTime, extension.DelayTime);
            Assert.Equal(expectedFields.TransparentColorIndex, extension.TransparentColorIndex);
        }
    }

    [Fact]
    public static void ReadPlainTextExtension_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadPlainTextExtension());

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadPlainTextExtension_InvalidBlockType_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x3B,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.Trailer);

            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadPlainTextExtension());

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadPlainTextExtension_InvalidExtensionLabel_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            var ex = Assert.Throws<InvalidOperationException>(() => reader.ReadPlainTextExtension());

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadPlainTextExtension_EndOfFile_ThrowsEndOfStreamException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0x01,
            0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.PlainText);

            var ex = Assert.Throws<EndOfStreamException>(() => reader.ReadPlainTextExtension());

            AssertIsErrorState(reader);
        }
    }

    [Fact]
    public static void ReadPlainTextExtension_InvalidBlockSize_ThrowsInvalidDataException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0x01,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.PlainText);

            var ex = Assert.Throws<InvalidDataException>(() => reader.ReadPlainTextExtension());

            Assert.Equal("Invalid block size.", ex.Message);
        }
    }

    public static readonly TheoryData<byte[], PlainTextExtensionFields> ReadPlainTextExtension_Valid_Data = new()
    {
        { new byte[] { 0x21, 0x01, 0x0C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, (0, 0, 0, 0, 0, 0, 0, 0) },
        { new byte[] { 0x21, 0x01, 0x0C, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B, 0x0C }, (0x0201, 0x0403, 0x0605, 0x0807, 0x09, 0x0A, 0x0B, 0x0C) },
    };

    [Theory]
    [MemberData(nameof(ReadPlainTextExtension_Valid_Data))]
    public static void ReadPlainTextExtension_Valid_ReturnsExpectedResults(byte[] blockBytes, PlainTextExtensionFields expectedFields)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.PlainText);

            var extension = reader.ReadPlainTextExtension();

            Assert.Equal(expectedFields.Left, extension.Left);
            Assert.Equal(expectedFields.Top, extension.Top);
            Assert.Equal(expectedFields.Width, extension.Width);
            Assert.Equal(expectedFields.Height, extension.Height);
            Assert.Equal(expectedFields.CellWidth, extension.CellWidth);
            Assert.Equal(expectedFields.CellHeight, extension.CellHeight);
            Assert.Equal(expectedFields.ForegroundColorIndex, extension.ForegroundColorIndex);
            Assert.Equal(expectedFields.BackgroundColorIndex, extension.BackgroundColorIndex);
        }
    }

    [Fact]
    public static void ReadApplicationExtension_InvalidApplicationIdentifierLength_ThrowsArgumentException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<ArgumentException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[7];
                Span<byte> applicationAuthenticationCode = stackalloc byte[3];
                reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            Assert.Equal("applicationIdentifier", ex.ParamName);
        }
    }

    [Fact]
    public static void ReadApplicationExtension_InvalidApplicationAuthenticationCodeLength_ThrowsArgumentException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<ArgumentException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[8];
                Span<byte> applicationAuthenticationCode = stackalloc byte[2];
                reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            Assert.Equal("applicationAuthenticationCode", ex.ParamName);
        }
    }

    [Fact]
    public static void ReadApplicationExtension_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[8];
                Span<byte> applicationAuthenticationCode = stackalloc byte[3];
                reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadApplicationExtension_InvalidBlockType_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x3B,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.Trailer);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[8];
                Span<byte> applicationAuthenticationCode = stackalloc byte[3];
                reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadApplicationExtension_InvalidExtensionLabel_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0x01,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.PlainText);

            var ex = Assert.Throws<InvalidOperationException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[8];
                Span<byte> applicationAuthenticationCode = stackalloc byte[3];
                reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadApplicationExtension_EndOfFile_ThrowsEndOfStreamException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            var ex = Assert.Throws<EndOfStreamException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[8];
                Span<byte> applicationAuthenticationCode = stackalloc byte[3];
                reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            AssertIsErrorState(reader);
        }
    }

    [Fact]
    public static void ReadApplicationExtension_InvalidBlockSize_ThrowsInvalidDataException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            var ex = Assert.Throws<InvalidDataException>(() =>
            {
                Span<byte> applicationIdentifier = stackalloc byte[8];
                Span<byte> applicationAuthenticationCode = stackalloc byte[3];
                reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            });

            Assert.Equal("Invalid block size.", ex.Message);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x21, 0xFF, 0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, new byte[] { 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x21, 0xFF, 0x0B, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B }, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 }, new byte[] { 0x09, 0x0A, 0x0B })]
    public static void ReadApplicationExtension_Valid_ReturnsExpectedResults(byte[] blockBytes, byte[] expectedApplicationIdentifier, byte[] expectedApplicationAuthenticationCode)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. blockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            Assert.Equal(expectedApplicationIdentifier, applicationIdentifier);
            Assert.Equal(expectedApplicationAuthenticationCode, applicationAuthenticationCode);
        }
    }

    [Fact]
    public static void ReadNetscapeApplicationExtensionSubblock_InvalidState_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            var ex = Assert.Throws<InvalidOperationException>(reader.ReadNetscapeApplicationExtensionSubblock);

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Fact]
    public static void ReadNetscapeApplicationExtensionSubblock_InvalidExtensionLabel_ThrowsInvalidOperationException()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFE,
            0x01, 0x00,
            0x01, 0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Comment);
            _ = reader.ReadSubblock();

            var ex = Assert.Throws<InvalidOperationException>(reader.ReadNetscapeApplicationExtensionSubblock);

            Assert.Equal(new InvalidOperationException().Message, ex.Message);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x04, 0x01, 0x00, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x06, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    public static void ReadNetscapeApplicationExtensionSubblock_InvalidSubblockSize_ThrowsInvalidDataException(byte[] subblockBytes)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. subblockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            var ex = Assert.Throws<InvalidDataException>(reader.ReadNetscapeApplicationExtensionSubblock);

            Assert.Equal("Invalid sub-block size.", ex.Message);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0x00, 0x00 })]
    [InlineData(new byte[] { 0x02, 0x03, 0x00 })]
    public static void ReadNetscapeApplicationExtensionSubblock_InvalidSubblockIdentifier_ThrowsInvalidDataException(byte[] subblockBytes)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. subblockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            var ex = Assert.Throws<InvalidDataException>(reader.ReadNetscapeApplicationExtensionSubblock);

            Assert.Equal("Invalid sub-block identifier.", ex.Message);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x03, 0x01, 0x00 })]
    [InlineData(new byte[] { 0x05, 0x02, 0x00, 0x00, 0x00 })]
    public static void ReadNetscapeApplicationExtensionSubblock_EndOfFile_ThrowsEndOfStreamException(byte[] subblockBytes)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. subblockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            var ex = Assert.Throws<EndOfStreamException>(reader.ReadNetscapeApplicationExtensionSubblock);

            AssertIsErrorState(reader);
        }
    }

    [Fact]
    public static void ReadNetscapeApplicationExtensionSubblock_BlockTerminator_ReturnsExpectedResults()
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            var subblock = reader.ReadNetscapeApplicationExtensionSubblock();

            Assert.Null(subblock);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x03, 0x01, 0x00, 0x00 }, 0x0000)]
    [InlineData(new byte[] { 0x03, 0x01, 0x01, 0x02 }, 0x0201)]
    public static void ReadNetscapeApplicationExtensionSubblock_LoopingSubblock_ReturnsExpectedResults(byte[] subblockBytes, ushort expectedLoopCount)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. subblockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            var subblock = reader.ReadNetscapeApplicationExtensionSubblock();

            var loopingSubblock = Assert.IsType<NetscapeApplicationExtension.LoopingSubblock>(subblock);
            Assert.Equal(expectedLoopCount, loopingSubblock.LoopCount);
        }
    }

    [Theory]
    [InlineData(new byte[] { 0x05, 0x02, 0x00, 0x00, 0x00, 0x00 }, 0x00000000)]
    [InlineData(new byte[] { 0x05, 0x02, 0x01, 0x02, 0x03, 0x04 }, 0x04030201)]
    public static void ReadNetscapeApplicationExtensionSubblock_BufferingSubblock_ReturnsExpectedResults(byte[] subblockBytes, uint expectedBufferLength)
    {
        byte[] bytes =
        [
            .. "GIF89a"u8,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x21, 0xFF,
            0x0B, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            .. subblockBytes
        ];

        using (var stream = new MemoryStream(bytes))
        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();

            _ = reader.ReadLogicalScreenDescriptor();

            var part = reader.Peek();
            Debug.Assert(part == GifReader.ReadPart.ExtensionLabel);
            var extensionLabel = reader.ReadExtensionLabel();
            Debug.Assert(extensionLabel == GifExtensionLabel.Application);

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

            var subblock = reader.ReadNetscapeApplicationExtensionSubblock();

            var loopingSubblock = Assert.IsType<NetscapeApplicationExtension.BufferingSubblock>(subblock);
            Assert.Equal(expectedBufferLength, loopingSubblock.BufferLength);
        }
    }

    private static void AssertIsErrorState(GifReader reader)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => reader.Peek());

        Assert.Equal(new InvalidOperationException().Message, ex.Message);
    }

    private sealed class UnreadableStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

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

    private sealed class ImageDataBlocksBuilder
    {
        private readonly byte _minCodeSize;

        private readonly Queue<byte> _codeBytes = new();
        private uint _bits = 0;
        private int _bitCount = 0;

        private int _freeCode;

        public ImageDataBlocksBuilder(byte minCodeSize)
        {
            Debug.Assert(minCodeSize >= 2 && minCodeSize <= 8);

            _minCodeSize = minCodeSize;

            int clearCode = 1 << _minCodeSize;
            int endCode = clearCode + 1;

            _freeCode = endCode + 1;
        }

        public void AddCode(int code)
        {
            int clearCode = 1 << _minCodeSize;
            int endCode = clearCode + 1;

            Debug.Assert(code < _freeCode);

            int codeSize = 32 - BitOperations.LeadingZeroCount((uint)_freeCode - 1);

            _bits |= (uint)code << _bitCount;
            _bitCount += codeSize;
            while (_bitCount >= 8)
                FlushByte();

            if (code == clearCode)
                _freeCode = endCode;
            else if (_freeCode < 1 << 12)
                _freeCode += 1;

        }

        public List<byte> MoveToBlockBytes()
        {
            int clearCode = 1 << _minCodeSize;
            int endCode = clearCode + 1;

            if (_bitCount > 0)
                FlushByte();

            var blockBytes = new List<byte>();
            blockBytes.Add(_minCodeSize);
            while (_codeBytes.Count > 0)
            {
                int blockLength = int.Min(_codeBytes.Count, 255);
                blockBytes.Add((byte)blockLength);
                for (int i = 0; i < blockLength; ++i)
                    blockBytes.Add(_codeBytes.Dequeue());
            }
            blockBytes.Add(0x00);

            _bits = 0;
            _bitCount = 0;

            _freeCode = 1 << endCode;

            return blockBytes;
        }

        private void FlushByte()
        {
            _codeBytes.Enqueue((byte)_bits);
            _bits >>= 8;
            _bitCount -= 8;
        }
    }
}
