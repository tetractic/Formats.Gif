// Copyright Carl Reinke
//
// This file is part of a library that is licensed under the terms of the GNU
// Lesser General Public License Version 3 as published by the Free Software
// Foundation.
//
// This license does not grant rights under trademark law for use of any trade
// names, trademarks, or service marks.

namespace Tetractic.Formats.Gif;

/// <summary>
/// Identifies a GIF extension.
/// </summary>
public enum GifExtensionLabel : byte
{
    /// <summary>
    /// A Plain Text Extension.
    /// </summary>
    /// <seealso cref="GifReader.ReadPlainTextExtension"/>
    /// <seealso cref="GifWriter.WritePlainTextExtension(GifPlainTextExtension)"/>
    PlainText = 0x01,

    /// <summary>
    /// A Graphic Control Extension.
    /// </summary>
    /// <seealso cref="GifReader.ReadGraphicControlExtension"/>
    /// <seealso cref="GifWriter.WriteGraphicControlExtension(GifGraphicControlExtension)"/>
    GraphicControl = 0xF9,

    /// <summary>
    /// A Comment Extension.
    /// </summary>
    /// <seealso cref="GifReader.ReadSubblock"/>
    /// <seealso cref="GifWriter.WriteExtensionLabel(GifExtensionLabel)"/>
    /// <seealso cref="GifWriter.WriteSubblock(System.ReadOnlySpan{byte})"/>
    /// <seealso cref="GifWriter.WriteBlockTerminator"/>
    Comment = 0xFE,

    /// <summary>
    /// An Application Extension.
    /// </summary>
    /// <seealso cref="GifReader.ReadApplicationExtension(System.Span{byte}, System.Span{byte})"/>
    /// <seealso cref="GifWriter.WriteApplicationExtension(System.ReadOnlySpan{byte}, System.ReadOnlySpan{byte})"/>
    Application = 0xFF,
}
