// Copyright Carl Reinke
//
// This file is part of a library that is licensed under the terms of the GNU
// Lesser General Public License Version 3 as published by the Free Software
// Foundation.
//
// This license does not grant rights under trademark law for use of any trade
// names, trademarks, or service marks.

using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;

namespace Tetractic.Formats.Gif;

/// <summary>
/// A reader that reads a GIF image from a stream in parts.
/// </summary>
/// <seealso href="https://www.w3.org/Graphics/GIF/spec-gif87.txt"/>
/// <seealso href="https://www.w3.org/Graphics/GIF/spec-gif89a.txt"/>
/// <remarks>
/// <code language="text">
/// ┌─┴──────┐
/// │ Header │                               ReadHeader()
/// └─┬──────┘
///   ▼
/// ┌─┴─────────────────────────┐
/// │ Logical Screen Descriptor │            ReadLogicalScreenDescriptor()
/// └─┬─────────────────────────┘
///   ▼
/// ┌─┴─────────────────────────┐
/// │ Global Color Table (opt.) │            ReadColorTable()
/// └─┬─────────────────────────┘
///   ▼
///   ├◄──────────────────────────────────┐  PeekBlockType()
///   │                                   │
///   │ 00-7F: Graphic Rendering blocks   │
///   │                                   │
///   │  ┌──────────────────────┐         │
///   ├─►┤ 2C: Image Descriptor │         │  ReadImageDescriptor()
///   │  └─┬────────────────────┘         │
///   │    ▼                              │
///   │  ┌─┴────────────────────────┐     │
///   │  │ Local Color Table (opt.) │     │  ReadColorTable()
///   │  └─┬────────────────────────┘     │
///   │    ▼                  ┌◄──┐       │
///   │  ┌─┴──────────┐  ┌────┴─┐ │       │
///   │  │ Image Data ├─►┤ Data ├─┴──────►┤  ReadImageData()
///   │  └────────────┘  └──────┘         ▲
///   │                                   │
///   │  ┌────────────────────────┐       │
///   ├─►┤ 21 01: Plain Text Ext. │       │  ReadExtensionLabel() and
///   │  └─┬──────────────────────┘       │  ReadPlainTextExtension()
///   │    ▼  ┌◄──┐                       │
///   │  ┌─┴──┴─┐ │                       │
///   │  │ Data ├─┴──────────────────────►┤  ReadBlock()
///   │  └──────┘                         ▲
///   │                                   │
///   │ 80-F9: Control blocks             │
///   │                                   │
///   │  ┌─────────────────────────────┐  │
///   ├─►┤ 21 F9: Graphic Control Ext. ├─►┤  ReadExtensionLabel() and
///   │  └─────────────────────────────┘  ▲  ReadGraphicControlExtension()
///   │                                   │
///   │ FA-FF: Special Purpose blocks     │
///   │                           ┌◄──┐   │
///   │  ┌────────────────────────┴─┐ │   │
///   ├─►┤ 21 FE: Comment Ext. Data ├─┴──►┤  ReadExtensionLabel() and
///   │  └──────────────────────────┘     ▲  ReadBlock()
///   │                                   │
///   │  ┌─────────────────────────┐      │
///   ├─►┤ 21 FF: Application Ext. │      │  ReadExtensionLabel() and
///   │  └─┬───────────────────────┘      │  ReadApplicationExtension(…)
///   │    ▼  ┌◄──┐                       │
///   │  ┌─┴──┴─┐ │                       │
///   │  │ Data ├─┴───────────────────────┘  ReadBlock()
///   │  └──────┘
///   ▼
/// ┌─┴───────────┐
/// │ 3B: Trailer │
/// └─────────────┘
/// </code>
/// </remarks>
public sealed class GifReader : IDisposable
{
    private readonly Stream _stream;

    private readonly bool _leaveOpen;

    private GifVersion _version;

    private State _state;
    private GifBlockLabel _blockLabel;
    private GifExtensionLabel _extensionLabel;

    private int _globalColorTableSize;
    private int _activeColorTableSize;

    private ushort _width;
    private ushort _height;

    /// <summary>
    /// Initializes a new <see cref="GifReader"/> instance.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="leaveOpen">Controls whether the stream remains open after the reader is
    ///     disposed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.
    ///     </exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> does not support reading.
    ///     </exception>
    public GifReader(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead)
            throw new ArgumentException("Stream must support reading.", nameof(stream));

        _stream = stream;
        _leaveOpen = leaveOpen;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_leaveOpen)
            _stream.Dispose();
    }

    /// <summary>
    /// Returns the type of the next block to read.
    /// </summary>
    /// <returns>The type of block to read.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state where the operation
    ///     can be performed.</exception>
    /// <exception cref="InvalidDataException">The data read from the stream is invalid.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    public BlockType PeekBlockType()
    {
        switch (_state)
        {
            case State.Header:
                return BlockType.Header;
            case State.LogicalScreenDescriptor:
                return BlockType.LogicalScreenDescriptor;
            case State.GlobalColorTable:
                return BlockType.GlobalColorTable;
            case State.BlockLabel:
            {
                try
                {
                    _blockLabel = (GifBlockLabel)ReadByteExactly(_stream);

                    switch (_blockLabel)
                    {
                        case GifBlockLabel.ExtensionIntroducer:
                        {
                            _state = State.ExtensionLabel;

                            return BlockType.Extension;
                        }
                        case GifBlockLabel.ImageSeparator:
                        {
                            _state = State.ImageDescriptor;

                            return BlockType.ImageDescriptor;
                        }
                        case GifBlockLabel.Trailer:
                        {
                            _state = State.Done;

                            return BlockType.Trailer;
                        }
                        default:
                            throw new InvalidDataException("An unrecognized block label was read.");
                    }
                }
                catch
                {
                    _state = State.Error;

                    throw;
                }
            }
            case State.ExtensionLabel:
                return BlockType.Extension;
            case State.ImageDescriptor:
                return BlockType.ImageDescriptor;
            case State.LocalColorTable:
                return BlockType.LocalColorTable;
            case State.ImageData:
                return BlockType.ImageData;
            case State.Block:
            case State.Subblock:
                return BlockType.Block;
            case State.Done:
                return BlockType.Trailer;
            default:
                throw new InvalidOperationException();
        }
    }

    /// <summary>
    /// Reads a Header.
    /// </summary>
    /// <returns>The header.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state where a Header can
    ///     be read.</exception>
    /// <exception cref="InvalidDataException">The data read from the stream is invalid.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    public GifVersion ReadHeader()
    {
        if (_state != State.Header)
            throw new InvalidOperationException();

        try
        {
            Span<byte> buffer = stackalloc byte[6];

            _stream.ReadExactly(buffer);

            if (!buffer.Slice(0, 3).SequenceEqual("GIF"u8))
                throw new InvalidDataException("Invalid format signature.");

            int version0 = buffer[3] - '0';
            int version1 = buffer[4] - '0';
            int version2 = buffer[5] - 'a';

            if ((uint)version0 >= 10 || (uint)version1 >= 10 || (uint)version2 >= 26)
                throw new InvalidDataException("Invalid format version.");

            int version01 = (version0 * 10 + version1 + (100 - 87)) % 100;
            var version = (GifVersion)(version01 * 26 + version2);

            _version = version;
            _state = State.LogicalScreenDescriptor;

            return version;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Reads a Logical Screen Descriptor.
    /// </summary>
    /// <returns>The descriptor.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state where where a
    ///     Logical Screen Descriptor can be read.</exception>
    /// <exception cref="InvalidDataException">The data read from the stream is invalid.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    public GifLogicalScreenDescriptor ReadLogicalScreenDescriptor()
    {
        if (_state != State.LogicalScreenDescriptor)
            throw new InvalidOperationException();

        try
        {
            Span<byte> buffer = stackalloc byte[7];

            _stream.ReadExactly(buffer);

            var descriptor = new GifLogicalScreenDescriptor
            {
                Width = BinaryPrimitives.ReadUInt16LittleEndian(buffer),
                Height = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2)),
                PackedFields = buffer[4],
                BackgroundColorIndex = buffer[5],
                PixelAspectRatio = buffer[6]
            };

            if (_version < GifVersion.Version89a)
            {
                if (descriptor.Sorted)
                    throw new InvalidDataException("Sorted color table is not valid for format version.");
                if (descriptor.PixelAspectRatio != 0)
                    throw new InvalidDataException("Pixel aspect ratio is not valid for format version.");
            }

            if (descriptor.HasGlobalColorTable)
            {
                _state = State.GlobalColorTable;
                _globalColorTableSize = 2 << descriptor.GlobalColorTableSize;
                _activeColorTableSize = _globalColorTableSize;
            }
            else
            {
                _state = State.BlockLabel;
            }

            return descriptor;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Reads a Global Color Table or a Local Color Table.
    /// </summary>
    /// <returns>The colors.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state where a Global
    ///     Color Table or a Local Color Table can be read.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    public GifColor[] ReadColorTable()
    {
        if (_state != State.GlobalColorTable &&
            _state != State.LocalColorTable)
        {
            throw new InvalidOperationException();
        }

        try
        {
            Span<byte> buffer = stackalloc byte[3 * _activeColorTableSize];

            _stream.ReadExactly(buffer);

            Span<byte> colorBuffer = buffer;

            var colors = new GifColor[_activeColorTableSize];

            for (int i = 0; i < colors.Length; ++i)
            {
                colors[i] = new GifColor(
                    r: colorBuffer[0],
                    g: colorBuffer[1],
                    b: colorBuffer[2]);

                colorBuffer = colorBuffer.Slice(3);
            }

            _state = _state == State.GlobalColorTable
                ? State.BlockLabel
                : State.ImageData;

            return colors;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Reads an Image Descriptor.
    /// </summary>
    /// <returns>The descriptor.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state where an Image
    ///     Descriptor can be read.</exception>
    /// <exception cref="InvalidDataException">The data read from the stream is invalid.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    public GifImageDescriptor ReadImageDescriptor()
    {
        if (_state != State.ImageDescriptor)
            throw new InvalidOperationException();

        try
        {
            Span<byte> block = stackalloc byte[10];

            _stream.ReadExactly(block.Slice(1));

            var descriptor = new GifImageDescriptor
            {
                Left = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(1)),
                Top = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(3)),
                Width = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(5)),
                Height = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(7)),
                PackedFields = block[9]
            };

            if (_version < GifVersion.Version89a)
            {
                if (descriptor.Sorted)
                    throw new InvalidDataException("Sorted color table is not valid for format version.");
            }
            if (_version <= GifVersion.Version89a)
            {
                if (descriptor.Reserved)
                    throw new InvalidDataException("Reserved bits are set.");
            }

            if (descriptor.HasLocalColorTable)
            {
                _state = State.LocalColorTable;
                _activeColorTableSize = 2 << descriptor.LocalColorTableSize;
            }
            else
            {
                _state = State.ImageData;
                _activeColorTableSize = _globalColorTableSize;
            }

            _width = descriptor.Width;
            _height = descriptor.Height;

            return descriptor;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Reads Table-Based Image Data.
    /// </summary>
    /// <returns>The image data, which is interlaced if applicable.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state where Table-Based
    ///     Image Data can be read.</exception>
    /// <exception cref="InvalidDataException">The data read from the stream is invalid.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    public byte[] ReadImageData()
    {
        if (_state != State.ImageData)
            throw new InvalidOperationException();

        byte[] imageData = new byte[_width * _height];

        try
        {
            byte rawCodeSize = ReadByteExactly(_stream);

            if (rawCodeSize < 2 || rawCodeSize > 8)
                throw new InvalidDataException("Invalid LZW code size.");

            ReadImageDataCore(_stream, rawCodeSize, imageData);

            _state = State.BlockLabel;

            return imageData;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Reads an extension label.
    /// </summary>
    /// <returns>The extension label.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state an extension label
    ///     can be read.</exception>
    /// <exception cref="InvalidDataException">The data read from the stream is invalid.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    public GifExtensionLabel ReadExtensionLabel()
    {
        if (_state != State.ExtensionLabel)
            throw new InvalidOperationException();

        try
        {
            _extensionLabel = (GifExtensionLabel)ReadByteExactly(_stream);

            _state = State.Block;

            switch (_extensionLabel)
            {
                case GifExtensionLabel.PlainText:
                case GifExtensionLabel.GraphicControl:
                case GifExtensionLabel.Comment:
                case GifExtensionLabel.Application:
                    if (_version < GifVersion.Version89a)
                        throw new InvalidDataException("Extension is not valid for format version.");
                    break;

                default:
                    if (_version <= GifVersion.Version89a)
                        throw new InvalidDataException("Extension is not valid for format version.");
                    break;
            }

            return _extensionLabel;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Reads a block or sub-block.
    /// </summary>
    /// <returns>The block or sub-block data; <see langword="null"/> if the block terminator was
    ///     read.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state where sub-blocks
    ///     can be read.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    public byte[]? ReadBlock()
    {
        if (_state != State.Block && _state != State.Subblock)
            throw new InvalidOperationException();

        try
        {
            byte length = ReadByteExactly(_stream);
            if (length == 0)
            {
                _state = State.BlockLabel;

                return null;
            }

            byte[] data = new byte[length];

            _stream.ReadExactly(data);

            _state = State.Subblock;

            return data;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Reads a Graphic Control Extension.
    /// </summary>
    /// <returns>The extension.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state where a Graphic
    ///     Control Extension can be read.</exception>
    /// <exception cref="InvalidDataException">The data read from the stream is invalid.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    /// <remarks>
    /// This extension has no sub-blocks and the block terminator is read by this method.
    /// </remarks>
    public GifGraphicControlExtension ReadGraphicControlExtension()
    {
        if (_state != State.Block ||
            _blockLabel != GifBlockLabel.ExtensionIntroducer ||
            _extensionLabel != GifExtensionLabel.GraphicControl)
        {
            throw new InvalidOperationException();
        }

        try
        {
            Span<byte> blocks = stackalloc byte[5 + 1];

            _stream.ReadExactly(blocks);

            Span<byte> block = blocks.Slice(0, 5);

            if (block[0] != 4)  // Block Size
                throw new InvalidDataException("Invalid block size.");

            var extension = new GifGraphicControlExtension
            {
                PackedFields = block[1],
                DelayTime = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(2)),
                TransparentColorIndex = block[4]
            };

            if (_version <= GifVersion.Version89a)
            {
                if (extension.Reserved)
                    throw new InvalidDataException("Reserved bits are set.");
                if (extension.DisposalMethod > GifDisposalMethod.RestoreToPrevious)
                    throw new InvalidDataException("Disposal method is undefined in format version.");
            }

            block = blocks.Slice(5, 1);

            if (block[0] != 0)  // Block Terminator
                throw new InvalidDataException("Unexpected sub-block.");

            _state = State.BlockLabel;

            return extension;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Reads a Plain Text Extension block.
    /// </summary>
    /// <returns>The extension.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state where a Plain Text
    ///     Extension can be read.</exception>
    /// <exception cref="InvalidDataException">The data read from the stream is invalid.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    /// <remarks>
    /// Use <see cref="ReadBlock"/> to read the sub-blocks of the extension, which contain the text
    /// data.
    /// </remarks>
    public GifPlainTextExtension ReadPlainTextExtension()
    {
        if (_state != State.Block ||
            _blockLabel != GifBlockLabel.ExtensionIntroducer ||
            _extensionLabel != GifExtensionLabel.PlainText)
        {
            throw new InvalidOperationException();
        }

        try
        {
            Span<byte> block = stackalloc byte[13];

            _stream.ReadExactly(block);

            if (block[0] != 12)  // Block Size
                throw new InvalidDataException("Invalid block size.");

            var extension = new GifPlainTextExtension
            {
                Left = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(1)),
                Top = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(3)),
                Width = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(5)),
                Height = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(7)),
                CellWidth = block[9],
                CellHeight = block[10],
                ForegroundColorIndex = block[11],
                BackgroundColorIndex = block[12],
            };

            _state = State.Subblock;

            return extension;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Reads an Application Extension block.
    /// </summary>
    /// <param name="applicationIdentifier">Returns the application identifier.</param>
    /// <param name="applicationAuthenticationCode">Returns the application authentication code.
    ///     </param>
    /// <exception cref="ArgumentException">The length of <paramref name="applicationIdentifier"/>
    ///     is not 8.</exception>
    /// <exception cref="ArgumentException">The length of
    ///     <paramref name="applicationAuthenticationCode"/> is not 3.</exception>
    /// <exception cref="InvalidOperationException">The reader is not in a state where an
    ///     Application Extension can be read.</exception>
    /// <exception cref="InvalidDataException">The data read from the stream is invalid.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    /// <remarks>
    /// Use <see cref="ReadBlock"/> to read the sub-blocks of the extension, which contain the
    /// application data.
    /// </remarks>
    /// <seealso cref="ReadNetscapeApplicationExtensionSubblock"/>
    public void ReadApplicationExtension(Span<byte> applicationIdentifier, Span<byte> applicationAuthenticationCode)
    {
        if (applicationIdentifier.Length != 8)
            throw new ArgumentException("Invalid length.", nameof(applicationIdentifier));
        if (applicationAuthenticationCode.Length != 3)
            throw new ArgumentException("Invalid length.", nameof(applicationAuthenticationCode));

        if (_state != State.Block ||
            _blockLabel != GifBlockLabel.ExtensionIntroducer ||
            _extensionLabel != GifExtensionLabel.Application)
        {
            throw new InvalidOperationException();
        }

        try
        {
            Span<byte> block = stackalloc byte[12];

            _stream.ReadExactly(block);

            if (block[0] != 11)  // Block Size
                throw new InvalidDataException("Invalid block size.");

            block.Slice(1, 8).CopyTo(applicationIdentifier);
            block.Slice(9, 3).CopyTo(applicationAuthenticationCode);

            _state = State.Subblock;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Reads a Netscape 2.0 Application Extension sub-block.
    /// </summary>
    /// <returns>The sub-block.</returns>
    /// <exception cref="InvalidOperationException">The reader is not in a state where a Netscape
    ///     2.0 Application Extension sub-block can be read.</exception>
    /// <exception cref="InvalidDataException">The data read from the stream is invalid.</exception>
    /// <exception cref="EndOfStreamException">The end of the stream is reached.</exception>
    /// <exception cref="IOException">An I/O error occurs when reading from the stream.</exception>
    public NetscapeApplicationExtension.Subblock? ReadNetscapeApplicationExtensionSubblock()
    {
        // https://github.com/mozilla/gecko-dev/blob/5836a062726f715fda621338a17b51aff30d0a8c/image/decoders/nsGIFDecoder2.cpp#L761

        if (_state != State.Subblock ||
            _blockLabel != GifBlockLabel.ExtensionIntroducer ||
            _extensionLabel != GifExtensionLabel.Application)
        {
            throw new InvalidOperationException();
        }

        try
        {
            Span<byte> block = stackalloc byte[1 + 255];

            byte length = ReadByteExactly(_stream);
            if (length == 0)
            {
                _state = State.BlockLabel;

                return null;
            }

            _stream.ReadExactly(block.Slice(1, length));

            switch (block[1])
            {
                case NetscapeApplicationExtension.LoopingSubblock.SubblockId:
                {
                    if (length != 3)
                        throw new InvalidDataException("Invalid sub-block size.");

                    return new NetscapeApplicationExtension.LoopingSubblock
                    {
                        LoopCount = BinaryPrimitives.ReadUInt16LittleEndian(block.Slice(2)),
                    };
                }
                case NetscapeApplicationExtension.BufferingSubblock.SubblockId:
                {
                    if (length != 5)
                        throw new InvalidDataException("Invalid sub-block size.");

                    return new NetscapeApplicationExtension.BufferingSubblock
                    {
                        BufferLength = BinaryPrimitives.ReadUInt32LittleEndian(block.Slice(2)),
                    };
                }
                default:
                    throw new InvalidDataException("Invalid sub-block identifier.");
            }
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <exception cref="IOException"/>
    /// <exception cref="EndOfStreamException"/>
    // ExceptionAdjustment: M:System.IO.Stream.ReadByte -T:System.NotSupportedException
    private static byte ReadByteExactly(Stream stream)
    {
        int temp = stream.ReadByte();
        if (temp < 0)
            throw new EndOfStreamException();

        return (byte)temp;
    }

    /// <exception cref="InvalidDataException"/>
    /// <exception cref="EndOfStreamException"/>
    /// <exception cref="IOException"/>
    private static void ReadImageDataCore(Stream stream, byte rawCodeSize, Span<byte> imageData)
    {
        // Raw codes are from 0 to (1 << rawCodeSize) - 1.
        // Clear code is 1 << rawCodeSize.
        // End code is 1 << rawCodeSize + 1.

        uint endCode = (1u << rawCodeSize) + 1;

        const uint maxCodes = 1 << 12;

        // Each entry is a 12-bit previous length, 12-bit previous code, and 8-bit byte.
        uint[] entries = new uint[maxCodes];
        for (int i = (1 << rawCodeSize) - 1; i >= 0; --i)
            entries[i] = (endCode << 8) | (uint)i;
        uint freeCode = endCode;
        uint previousCode = endCode;
        uint previousLength = 0;

        int codeSize = rawCodeSize + 1;
        uint codeMask = (1u << codeSize) - 1;

        uint bits = 0;
        int bitCount = 0;

        Span<byte> buffer = stackalloc byte[255];
        scoped Span<byte> block = [];

        for (int i = 0; ;)
        {
            while (bitCount < codeSize)
            {
                if (block.Length == 0)
                {
                    byte blockLength = ReadByteExactly(stream);
                    if (blockLength == 0)
                        throw new InvalidDataException("LZW code stream is truncated.");

                    block = buffer.Slice(0, blockLength);
                    stream.ReadExactly(block);
                }

                if (block.Length >= 2)
                {
                    bits |= (uint)BinaryPrimitives.ReadUInt16LittleEndian(block) << bitCount;
                    bitCount += 16;
                    block = block.Slice(2);
                    break;
                }
                else
                {
                    bits |= (uint)block[0] << bitCount;
                    bitCount += 8;
                    block = block.Slice(1);
                }
            }

            uint code = bits & codeMask;
            bits >>= codeSize;
            bitCount -= codeSize;

            if (code == endCode)
            {
                if (bitCount >= 8 || block.Length > 0)
                    throw new InvalidDataException("LZW code stream has trailing garbage.");

                byte blockLength = ReadByteExactly(stream);
                if (blockLength != 0)
                    throw new InvalidDataException("LZW code stream has trailing garbage.");

                if (i != imageData.Length)
                    throw new InvalidDataException("LZW code stream produced too little data.");

                return;
            }
            else if (code == endCode - 1)  // Clear code.
            {
                freeCode = endCode;
                previousCode = endCode;
                previousLength = 0;

                codeSize = rawCodeSize + 1;
                codeMask = (1u << codeSize) - 1;

                continue;
            }

            byte entryByte;

            if (code > freeCode)
                throw new InvalidDataException("Invalid LZW code.");

            uint entryCode;

            if (code == freeCode)
            {
                Debug.Assert(previousCode != endCode);

                // Output "b...b" where "b..." is the output for the previous code.
                // The output for a code is produced in reverse order, but "b" won't be known until
                // the end, so the last "b" will be output after the first "b".
                entryCode = previousCode;
            }
            else
            {
                entryCode = code;
            }

            uint entry = entries[entryCode];
            entryByte = (byte)entry;
            entryCode = (entry >> 8) & 0xFFF;

            uint length = (entry >> (12 + 8)) + 1;

            if (imageData.Length - i < length)
                throw new InvalidDataException("LZW code stream produced too much data.");

            var outputBuffer = imageData.Slice(i, (int)length);
            i += (int)length;

            int j = (int)length - 1;
            outputBuffer[j] = entryByte;

            while (j > 0)
            {
                Debug.Assert(entryCode != endCode);

                entry = entries[entryCode];
                entryByte = (byte)entry;
                entryCode = (entry >> 8) & 0xFFF;

                j -= 1;
                outputBuffer[j] = entryByte;
            }

            Debug.Assert(entryCode == endCode);

            // Output last "b" of "b...b".
            if (code == freeCode)
            {
                length += 1;

                if (i == imageData.Length)
                    throw new InvalidDataException("LZW code stream produced too much data.");

                imageData[i] = entryByte;
                i += 1;
            }

            if (freeCode < maxCodes)
            {
                // For the first code in the stream after the clear code, the free code is actually
                // the end code, so a bogus entry will be written but cannot be looked up.

                Debug.Assert(previousLength < (1 << 12));

                // Assign new code.
                entries[freeCode] = (previousLength << (12 + 8)) | (previousCode << 8) | entryByte;
                freeCode += 1;
                previousCode = code;
                previousLength = length;

                // Increase code size if necessary.
                if (freeCode < maxCodes)
                {
                    codeSize += (int)(freeCode >> codeSize);
                    codeMask = (1u << codeSize) - 1;
                }
            }
        }
    }

    /// <summary>
    /// Represents the type of block to read next.
    /// </summary>
    public enum BlockType
    {
        /// <summary>
        /// A Header.
        /// </summary>
        /// <seealso cref="ReadHeader"/>
        Header,

        /// <summary>
        /// A Logical Screen Descriptor.
        /// </summary>
        /// <seealso cref="ReadLogicalScreenDescriptor"/>
        LogicalScreenDescriptor,

        /// <summary>
        /// A Global Color Table.
        /// </summary>
        /// <seealso cref="ReadColorTable"/>
        GlobalColorTable,

        /// <summary>
        /// An Image Descriptor.
        /// </summary>
        /// <seealso cref="ReadImageDescriptor"/>
        ImageDescriptor,

        /// <summary>
        /// A Local Color Table.
        /// </summary>
        /// <seealso cref="ReadColorTable"/>
        LocalColorTable,

        /// <summary>
        /// Table-Based Image Data.
        /// </summary>
        /// <seealso cref="ReadImageData"/>
        ImageData,

        /// <summary>
        /// An extension.
        /// </summary>
        /// <seealso cref="ReadExtensionLabel"/>
        Extension,

        /// <summary>
        /// A block or sub-block.
        /// </summary>
        /// <seealso cref="ReadBlock"/>
        Block,

        /// <summary>
        /// A Trailer, which indicates the end of the data stream.
        /// </summary>
        Trailer,
    }

    private enum State
    {
        Header,
        LogicalScreenDescriptor,
        GlobalColorTable,
        BlockLabel,
        ExtensionLabel,
        ImageDescriptor,
        LocalColorTable,
        ImageData,
        Block,
        Subblock,
        Done,
        Error,
    }
}
