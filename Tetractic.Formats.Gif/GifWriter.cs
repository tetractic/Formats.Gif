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
/// A writer that writes a GIF image to a stream in parts.
/// </summary>
/// <seealso href="https://www.w3.org/Graphics/GIF/spec-gif87.txt"/>
/// <seealso href="https://www.w3.org/Graphics/GIF/spec-gif89a.txt"/>
/// <remarks>
/// <code language="text">
/// ┌─┴──────┐
/// │ Header │                               WriteHeader(…)
/// └─┬──────┘
///   ▼
/// ┌─┴─────────────────────────┐
/// │ Logical Screen Descriptor │            WriteLogicalScreenDescriptor(…)
/// └─┬─────────────────────────┘
///   ▼
/// ┌─┴─────────────────────────┐
/// │ Global Color Table (opt.) │            WriteColorTable(…)
/// └─┬─────────────────────────┘
///   ▼
///   ├◄──────────────────────────────────┐
///   │                                   │
///   │ 00-7F: Graphic Rendering blocks   │
///   │                                   │
///   │  ┌──────────────────────┐         │
///   ├─►┤ 2C: Image Descriptor │         │  WriteImageDescriptor(…)
///   │  └─┬────────────────────┘         │
///   │    ▼                              │
///   │  ┌─┴────────────────────────┐     │
///   │  │ Local Color Table (opt.) │     │  WriteColorTable(…)
///   │  └─┬────────────────────────┘     │
///   │    ▼                  ┌◄──┐       │
///   │  ┌─┴──────────┐  ┌────┴─┐ │       │
///   │  │ Image Data ├─►┤ Data ├─┴──────►┤  WriteImageData(…)
///   │  └────────────┘  └──────┘         ▲
///   │                                   │
///   │  ┌────────────────────────┐       │
///   ├─►┤ 21 01: Plain Text Ext. │       │  WritePlainTextExtension(…)
///   │  └─┬──────────────────────┘       │
///   │    ▼  ┌◄──┐                       │
///   │  ┌─┴──┴─┐ │                       │
///   │  │ Data ├─┴──────────────────────►┤  WriteSubblock(…) and
///   │  └──────┘                         ▲  WriteBlockTerminator()
///   │                                   │
///   │ 80-F9: Control blocks             │
///   │                                   │
///   │  ┌─────────────────────────────┐  │
///   ├─►┤ 21 F9: Graphic Control Ext. ├─►┤  WriteGraphicControlExtension(…) and
///   │  └─────────────────────────────┘  ▲  WriteBlockTerminator()
///   │                                   │
///   │ FA-FF: Special Purpose blocks     │
///   │                           ┌◄──┐   │
///   │  ┌────────────────────────┴─┐ │   │  WriteExtensionLabel(…),
///   ├─►┤ 21 FE: Comment Ext. Data ├─┴──►┤  WriteSubblock(…), and
///   │  └──────────────────────────┘     ▲  WriteBlockTerminator()
///   │                                   │
///   │  ┌─────────────────────────┐      │
///   ├─►┤ 21 FF: Application Ext. │      │  WriteApplicationExtension(…)
///   │  └─┬───────────────────────┘      │
///   │    ▼  ┌◄──┐                       │
///   │  ┌─┴──┴─┐ │                       │
///   │  │ Data ├─┴───────────────────────┘  WriteSubblock(…) and
///   │  └──────┘                            WriteBlockTerminator()
///   ▼
/// ┌─┴───────────┐
/// │ 3B: Trailer │                          WriteTrailer()
/// └─────────────┘
/// </code>
/// </remarks>
public sealed class GifWriter : IDisposable
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
    /// Initializes a new <see cref="GifWriter"/> instance.
    /// </summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="leaveOpen">Controls whether the stream remains open after the writer is
    ///     disposed.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.
    ///     </exception>
    /// <exception cref="ArgumentException"><paramref name="stream"/> does not support writing.
    ///     </exception>
    public GifWriter(Stream stream, bool leaveOpen = false)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite)
            throw new ArgumentException("Stream must support writing.", nameof(stream));

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
    /// Writes a Header.
    /// </summary>
    /// <param name="version">The GIF version.</param>
    /// <exception cref="ArgumentException"><paramref name="version"/> is invalid.</exception>
    /// <exception cref="InvalidOperationException">The writer is not in a state where a Header can
    ///     be written.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WriteHeader(GifVersion version)
    {
        if ((uint)version >= 10 * 10 * 26)
            throw new ArgumentException("Invalid version.", nameof(version));

        if (_state != State.Header)
            throw new InvalidOperationException();

        try
        {
            Span<byte> buffer = stackalloc byte[6];

            "GIF"u8.CopyTo(buffer);  // Signature

            int version01 = ((int)version / 26 + 87) % 100;
            int version2 = (int)version % 26;

            buffer[3] = (byte)(version01 / 10 + '0');
            buffer[4] = (byte)(version01 % 10 + '0');
            buffer[5] = (byte)(version2 + 'a');

            _stream.Write(buffer);

            _version = version;
            _state = State.LogicalScreenDescriptor;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes a Logical Screen Descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <exception cref="InvalidOperationException">The descriptor specifies a value that is not
    ///     supported by the GIF version.</exception>
    /// <exception cref="InvalidOperationException">The writer is not in a state where a Logical
    ///     Screen Descriptor can be written.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WriteLogicalScreenDescriptor(GifLogicalScreenDescriptor descriptor)
    {
        if (_state != State.LogicalScreenDescriptor)
            throw new InvalidOperationException();

        if (_version < GifVersion.Version89a)
        {
            if (descriptor.Sorted)
                throw new InvalidOperationException("Sorted color table is not valid for format version.");
            if (descriptor.PixelAspectRatio != 0)
                throw new InvalidOperationException("Pixel aspect ratio is not valid for format version.");
        }

        try
        {
            Span<byte> buffer = stackalloc byte[7];

            BinaryPrimitives.WriteUInt16LittleEndian(buffer, descriptor.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(2), descriptor.Height);
            buffer[4] = descriptor.PackedFields;
            buffer[5] = descriptor.BackgroundColorIndex;
            buffer[6] = descriptor.PixelAspectRatio;

            _stream.Write(buffer);

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
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes a Global Color Table or a Local Color Table.
    /// </summary>
    /// <param name="colors">The colors.  If the length is less than the size of the color table
    ///     then the remaining colors will be black.</param>
    /// <exception cref="InvalidOperationException">The writer is not in a state where a Global
    ///     Color Table or a Local Color Table can be written.</exception>
    /// <exception cref="InvalidOperationException">The length of <paramref name="colors"/> is
    ///     greater than the size of the color table.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WriteColorTable(ReadOnlySpan<GifColor> colors)
    {
        if (_state != State.GlobalColorTable &&
            _state != State.LocalColorTable)
        {
            throw new InvalidOperationException();
        }

        if (colors.Length > _activeColorTableSize)
            throw new InvalidOperationException("Too many colors.");

        try
        {
            Span<byte> buffer = stackalloc byte[3 * _activeColorTableSize];

            Span<byte> colorBuffer = buffer;

            for (int i = 0; i < colors.Length; ++i)
            {
                var color = colors[i];

                colorBuffer[0] = color.R;
                colorBuffer[1] = color.G;
                colorBuffer[2] = color.B;

                colorBuffer = colorBuffer.Slice(3);
            }

            _stream.Write(buffer);

            _state = _state == State.GlobalColorTable
                ? State.BlockLabel
                : State.ImageData;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes an Image Descriptor.
    /// </summary>
    /// <param name="descriptor">The descriptor.</param>
    /// <exception cref="InvalidOperationException">The descriptor specifies a value that is not
    ///     supported by the GIF version.</exception>
    /// <exception cref="InvalidOperationException">The writer is not in a state where an Image
    ///     Descriptor can be written.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WriteImageDescriptor(GifImageDescriptor descriptor)
    {
        if (_state != State.BlockLabel)
            throw new InvalidOperationException();

        if (_version < GifVersion.Version89a)
        {
            if (descriptor.Sorted)
                throw new InvalidOperationException("Sorted color table is not valid for format version.");
        }
        if (_version <= GifVersion.Version89a)
        {
            if (descriptor.Reserved)
                throw new InvalidOperationException("Reserved bits are set.");
        }

        _blockLabel = GifBlockLabel.ImageSeparator;

        try
        {
            Span<byte> buffer = stackalloc byte[10];

            buffer[0] = (byte)GifBlockLabel.ImageSeparator;
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(1), descriptor.Left);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(3), descriptor.Top);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(5), descriptor.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(buffer.Slice(7), descriptor.Height);
            buffer[9] = descriptor.PackedFields;

            _stream.Write(buffer);

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
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes Table-Based Image Data.
    /// </summary>
    /// <param name="imageData">The image data, which must already be interlaced if applicable.
    ///     </param>
    /// <exception cref="InvalidOperationException">The length of <paramref name="imageData"/> does
    ///     not match the dimensions that were specified in the Image Descriptor.</exception>
    /// <exception cref="InvalidOperationException">The writer is not in a state where Table-Based
    ///     Image Data can be written.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WriteImageData(ReadOnlySpan<byte> imageData)
    {
        if (imageData.Length != _width * _height)
            throw new InvalidOperationException("Invalid image data length for image dimensions.");

        byte usedBits = 0;
        for (int i = 0; i < imageData.Length; ++i)
            usedBits |= imageData[i];

        byte minCodeSize = byte.Max(2, (byte)(8 - byte.LeadingZeroCount(usedBits)));

        WriteImageDataHeader(minCodeSize);

        Debug.Assert(_state == State.Subblock0);

        try
        {
            WriteImageDataCore(imageData, minCodeSize, _stream);

            _state = State.BlockLabel;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes the header for Table-Based Image Data.
    /// </summary>
    /// <param name="minCodeSize">The LZW minimum code size.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minCodeSize"/> is less than 2
    ///     or is greater than 8.</exception>
    /// <exception cref="InvalidOperationException">The writer is not in a state where Table-Based
    ///     Image Data can be written.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    /// <remarks>
    /// Use <see cref="WriteSubblock(ReadOnlySpan{byte})"/> and <see cref="WriteBlockTerminator"/>
    /// to write the sub-blocks of the block, which contain the LZW code stream.
    /// </remarks>
    /// <seealso cref="WriteImageData(ReadOnlySpan{byte})"/>
    // ExceptionAdjustment: M:System.IO.Stream.WriteByte(System.Byte) -T:System.NotSupportedException
    public void WriteImageDataHeader(byte minCodeSize)
    {
        if (minCodeSize < 2 || minCodeSize > 8)
            throw new ArgumentOutOfRangeException(nameof(minCodeSize));

        if (_state != State.ImageData)
            throw new InvalidOperationException();

        try
        {
            _stream.WriteByte(minCodeSize);

            _state = State.Subblock0;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes an extension label.
    /// </summary>
    /// <param name="label">The extension label.</param>
    /// <exception cref="InvalidOperationException">The writer is not in a state where an extension
    ///     label can be written.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    /// <remarks>
    /// Use <see cref="WriteSubblock(ReadOnlySpan{byte})"/> and <see cref="WriteBlockTerminator"/>
    /// to write the sub-blocks of the block.
    /// </remarks>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WriteExtensionLabel(GifExtensionLabel label)
    {
        if (_state != State.BlockLabel)
            throw new InvalidOperationException();

        switch (label)
        {
            case GifExtensionLabel.PlainText:
            case GifExtensionLabel.GraphicControl:
            case GifExtensionLabel.Comment:
            case GifExtensionLabel.Application:
                if (_version < GifVersion.Version89a)
                    throw new InvalidOperationException("Not valid for format version.");
                break;

            default:
                if (_version <= GifVersion.Version89a)
                    throw new InvalidOperationException("Not valid for format version.");
                break;
        }

        _blockLabel = GifBlockLabel.ExtensionIntroducer;
        _extensionLabel = label;

        try
        {
            Span<byte> buffer =
            [
                (byte)GifBlockLabel.ExtensionIntroducer,
                (byte)label,
            ];

            _stream.Write(buffer);

            _state = State.Subblock0;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes a sub-block.
    /// </summary>
    /// <param name="data">The sub-block data.</param>
    /// <exception cref="ArgumentException">The length of <paramref name="data"/> is 0 or is greater
    ///     than 255.</exception>
    /// <exception cref="InvalidOperationException">The writer is not in a state where a sub-block
    ///     can be written.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WriteSubblock(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0 || data.Length > byte.MaxValue)
            throw new ArgumentException("Invalid length.", nameof(data));

        if (_state != State.Subblock0 && _state != State.Subblocks)
            throw new InvalidOperationException();

        try
        {
            Span<byte> block = stackalloc byte[1 + data.Length];

            block[0] = (byte)data.Length;  // Block Size
            data.CopyTo(block.Slice(1));

            _stream.Write(block);

            _state = State.Subblocks;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes a block terminator.
    /// </summary>
    /// <exception cref="InvalidOperationException">The writer is not in a state where a block
    ///     terminator can be written.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    // ExceptionAdjustment: M:System.IO.Stream.WriteByte(System.Byte) -T:System.NotSupportedException
    public void WriteBlockTerminator()
    {
        if (_state != State.Subblock0 && _state != State.Subblocks)
            throw new InvalidOperationException();

        try
        {
            _stream.WriteByte(0);  // Block Terminator

            _state = State.BlockLabel;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes the extension label and first sub-block of a Graphic Control Extension.
    /// </summary>
    /// <param name="extension">The sub-block.</param>
    /// <exception cref="InvalidOperationException">The writer is not in a state where a Graphic
    ///     Control Extension can be written.</exception>
    /// <exception cref="InvalidOperationException">The extensions is not supported by the GIF
    ///     version.</exception>
    /// <exception cref="InvalidOperationException">The extension specifies a value that is not
    ///     supported by the GIF version.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    /// <remarks>
    /// Use <see cref="WriteSubblock(ReadOnlySpan{byte})"/> and <see cref="WriteBlockTerminator"/>
    /// to write the sub-blocks of the block.  There should be none.
    /// </remarks>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WriteGraphicControlExtension(GifGraphicControlExtension extension)
    {
        if (_state != State.BlockLabel)
            throw new InvalidOperationException();

        if (_version < GifVersion.Version89a)
            throw new InvalidOperationException("Not valid for format version.");
        if (_version <= GifVersion.Version89a)
        {
            if (extension.Reserved)
                throw new InvalidOperationException("Reserved bits are set.");
            if (extension.DisposalMethod > GifDisposalMethod.RestoreToPrevious)
                throw new InvalidOperationException("Disposal method is undefined in format version.");
        }

        _blockLabel = GifBlockLabel.ExtensionIntroducer;
        _extensionLabel = GifExtensionLabel.GraphicControl;

        try
        {
            Span<byte> buffer = stackalloc byte[2 + 5];

            buffer[0] = (byte)GifBlockLabel.ExtensionIntroducer;
            buffer[1] = (byte)GifExtensionLabel.GraphicControl;

            Span<byte> block = buffer.Slice(2, 5);

            block[0] = 4;  // Block Size
            block[1] = extension.PackedFields;
            BinaryPrimitives.WriteUInt16LittleEndian(block.Slice(2), extension.DelayTime);
            block[4] = extension.TransparentColorIndex;

            _stream.Write(buffer);

            _state = State.Subblocks;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes the extension label and the first sub-block of a Plain Text Extension.
    /// </summary>
    /// <param name="extension">The sub-block.</param>
    /// <exception cref="InvalidOperationException">The writer is not in a state where a Plain Text
    ///     Extension can be written.</exception>
    /// <exception cref="InvalidOperationException">The extension is not supported by the GIF
    ///     version.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    /// <remarks>
    /// Use <see cref="WriteSubblock(ReadOnlySpan{byte})"/> and <see cref="WriteBlockTerminator"/>
    /// to write the sub-blocks of the block, which contain the text data.
    /// </remarks>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WritePlainTextExtension(GifPlainTextExtension extension)
    {
        if (_state != State.BlockLabel)
            throw new InvalidOperationException();

        if (_version < GifVersion.Version89a)
            throw new InvalidOperationException("Not valid for format version.");

        _blockLabel = GifBlockLabel.ExtensionIntroducer;
        _extensionLabel = GifExtensionLabel.PlainText;

        try
        {
            Span<byte> buffer = stackalloc byte[2 + 13];

            buffer[0] = (byte)GifBlockLabel.ExtensionIntroducer;
            buffer[1] = (byte)GifExtensionLabel.PlainText;

            Span<byte> block = buffer.Slice(2, 13);

            block[0] = 12;  // Block Size
            BinaryPrimitives.WriteUInt16LittleEndian(block.Slice(1), extension.Left);
            BinaryPrimitives.WriteUInt16LittleEndian(block.Slice(3), extension.Top);
            BinaryPrimitives.WriteUInt16LittleEndian(block.Slice(5), extension.Width);
            BinaryPrimitives.WriteUInt16LittleEndian(block.Slice(7), extension.Height);
            block[9] = extension.CellWidth;
            block[10] = extension.CellHeight;
            block[11] = extension.ForegroundColorIndex;
            block[12] = extension.BackgroundColorIndex;

            _stream.Write(buffer);

            _state = State.Subblocks;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes the the extension label and first sub-block of an Application Extension.
    /// </summary>
    /// <param name="applicationIdentifier">The application identifier.</param>
    /// <param name="applicationAuthenticationCode">The application authentication code.</param>
    /// <exception cref="ArgumentException">The length of <paramref name="applicationIdentifier"/>
    ///     is not 8.</exception>
    /// <exception cref="ArgumentException">The length of
    ///     <paramref name="applicationAuthenticationCode"/> is not 3.</exception>
    /// <exception cref="InvalidOperationException">The writer is not in a state where an
    ///     Application Extension can be written.</exception>
    /// <exception cref="InvalidOperationException">The extension is not supported by the GIF
    ///     version.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    /// <remarks>
    /// Use <see cref="WriteSubblock(ReadOnlySpan{byte})"/> and <see cref="WriteBlockTerminator"/>
    /// to write the sub-blocks of the block, which contain the application data.
    /// </remarks>
    /// <seealso cref="WriteNetscapeApplicationExtensionSubblock"/>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WriteApplicationExtension(ReadOnlySpan<byte> applicationIdentifier, ReadOnlySpan<byte> applicationAuthenticationCode)
    {
        if (applicationIdentifier.Length != 8)
            throw new ArgumentException("Invalid length.", nameof(applicationIdentifier));
        if (applicationAuthenticationCode.Length != 3)
            throw new ArgumentException("Invalid length.", nameof(applicationAuthenticationCode));

        if (_state != State.BlockLabel)
            throw new InvalidOperationException();

        if (_version < GifVersion.Version89a)
            throw new InvalidOperationException("Not valid for format version.");

        _blockLabel = GifBlockLabel.ExtensionIntroducer;
        _extensionLabel = GifExtensionLabel.Application;

        try
        {
            Span<byte> buffer = stackalloc byte[2 + 12];

            buffer[0] = (byte)GifBlockLabel.ExtensionIntroducer;
            buffer[1] = (byte)GifExtensionLabel.Application;

            Span<byte> block = buffer.Slice(2, 12);

            block[0] = 11;  // Block Size
            applicationIdentifier.CopyTo(block.Slice(1, 8));
            applicationAuthenticationCode.CopyTo(block.Slice(9, 3));

            _stream.Write(buffer);

            _state = State.Subblocks;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes a sub-block of a Netscape 2.0 Application Extension.
    /// </summary>
    /// <param name="subblock">The sub-block.</param>
    /// <exception cref="ArgumentNullException"><paramref name="subblock"/> is
    ///     <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The writer is not in a state where a sub-block
    ///     of an Application Extension can be written.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    // ExceptionAdjustment: M:System.IO.Stream.WriteByte(System.Byte) -T:System.NotSupportedException
    public void WriteNetscapeApplicationExtensionSubblock(NetscapeApplicationExtension.Subblock subblock)
    {
        ArgumentNullException.ThrowIfNull(subblock);

        // https://github.com/mozilla/gecko-dev/blob/5836a062726f715fda621338a17b51aff30d0a8c/image/decoders/nsGIFDecoder2.cpp#L761

        if (_state != State.Subblocks ||
            _blockLabel != GifBlockLabel.ExtensionIntroducer ||
            _extensionLabel != GifExtensionLabel.Application)
        {
            throw new InvalidOperationException();
        }

        try
        {
            switch (subblock)
            {
                case NetscapeApplicationExtension.LoopingSubblock loopingSubblock:
                {
                    Span<byte> block = stackalloc byte[4];

                    block[0] = 3;  // Sub-block Size
                    block[1] = NetscapeApplicationExtension.LoopingSubblock.SubblockId;
                    BinaryPrimitives.WriteUInt16LittleEndian(block.Slice(2), loopingSubblock.LoopCount);

                    _stream.Write(block);
                    break;
                }
                case NetscapeApplicationExtension.BufferingSubblock bufferingSubblock:
                {
                    Span<byte> block = stackalloc byte[6];

                    block[0] = 5;  // Sub-block Size
                    block[1] = NetscapeApplicationExtension.BufferingSubblock.SubblockId;
                    BinaryPrimitives.WriteUInt32LittleEndian(block.Slice(2), bufferingSubblock.BufferLength);

                    _stream.Write(block);
                    break;
                }
                default:
                    throw new UnreachableException();
            }
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <summary>
    /// Writes a Trailer.
    /// </summary>
    /// <exception cref="InvalidOperationException">The writer is not in a state where a Trailer can
    ///     be written.</exception>
    /// <exception cref="IOException">An I/O error occurs when writing to the stream.</exception>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    public void WriteTrailer()
    {
        if (_state != State.BlockLabel)
            throw new InvalidOperationException();

        try
        {
            Span<byte> buffer =
            [
                (byte)GifBlockLabel.Trailer,
            ];

            _stream.Write(buffer);

            _state = State.Done;
        }
        catch
        {
            _state = State.Error;

            throw;
        }
    }

    /// <exception cref="IOException"/>
    // ExceptionAdjustment: M:System.IO.Stream.Write(System.ReadOnlySpan{System.Byte}) -T:System.NotSupportedException
    // ExceptionAdjustment: M:System.IO.Stream.WriteByte(System.Byte) -T:System.NotSupportedException
    private static void WriteImageDataCore(ReadOnlySpan<byte> imageData, byte rawCodeSize, Stream stream)
    {
        // Raw codes are from 0 to (1 << rawCodeSize) - 1.
        // Clear code is 1 << rawCodeSize.
        // End code is 1 << rawCodeSize + 1.

        uint endCode = (1u << rawCodeSize) + 1;

        const uint maxCodes = 1 << 12;

        ushort[] codes = new ushort[maxCodes << rawCodeSize];
        codes.AsSpan().Fill((ushort)endCode);
        uint freeCode = endCode + 1;

        int codeSize = rawCodeSize + 1;

        // Output initial clear code.
        uint bits = endCode - 1;
        int bitCount = codeSize;

        int blockLength = 0;

        Span<byte> buffer = stackalloc byte[1 + 255];

        bool ended = false;

        ushort noNextCode = 0;

        for (int i = 0; ;)
        {
            // Output bytes to block.
            while (bitCount >= 8)
            {
                Debug.Assert(bitCount <= 32);

                blockLength += 1;
                buffer[blockLength] = (byte)bits;
                bits >>= 8;
                bitCount -= 8;

                if (blockLength == 255)
                {
                    buffer[0] = 255;
                    stream.Write(buffer);
                    blockLength = 0;
                }
            }

            if (i < imageData.Length)
            {
                byte nextByte = imageData[i];
                ref ushort nextCode = ref noNextCode;

                i += 1;
                uint code = nextByte;

                // Find code.
                while (i < imageData.Length)
                {
                    nextByte = imageData[i];
                    nextCode = ref codes[(code << rawCodeSize) | nextByte];

                    if (nextCode == endCode)
                        break;

                    i += 1;
                    code = nextCode;
                }

                if (freeCode == maxCodes)
                {
                    // Defer clearing full dictionary if there is a match.
                    if (code > endCode)
                    {
                        // Output code.
                        bits |= code << bitCount;
                        bitCount += codeSize;

                        continue;
                    }

                    // Output clear code.
                    bits |= (endCode - 1) << bitCount;
                    bitCount += codeSize;

                    codes.AsSpan(0, (int)freeCode << rawCodeSize).Fill((ushort)endCode);
                    freeCode = endCode + 1;

                    codeSize = rawCodeSize + 1;

                    // Code still exists in the cleared dictionary.
                }

                // Output code.
                bits |= code << bitCount;
                bitCount += codeSize;

                // Increase code size if necessary.
                if (freeCode < maxCodes)
                    codeSize += (int)(freeCode >> codeSize);

                // Assign new code.
                nextCode = (ushort)freeCode;
                freeCode += 1;
            }
            else
            {
                if (ended)
                    break;

                // Output end code.
                bits |= endCode << bitCount;
                bitCount += codeSize;

                ended = true;
            }
        }

        // Flush bits to block; flush block.
        {
            Debug.Assert(bitCount <= 8);

            if (bitCount > 0)
            {
                blockLength += 1;
                buffer[blockLength] = (byte)bits;
            }

            if (blockLength > 0)
            {
                buffer[0] = (byte)blockLength;
                stream.Write(buffer.Slice(0, 1 + blockLength));
            }
        }

        stream.WriteByte(0);  // Block Terminator
    }

    private enum State
    {
        Header,
        LogicalScreenDescriptor,
        GlobalColorTable,
        BlockLabel,
        LocalColorTable,
        ImageData,
        Subblock0,
        Subblocks,
        Done,
        Error,
    }
}
