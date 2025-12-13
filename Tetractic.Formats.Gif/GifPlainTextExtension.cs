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
/// The first sub-block of a Plain Text Extension, which describes how plain text is to be
/// rendered.  The text is in the remaining sub-blocks of the extension block.
/// </summary>
public readonly struct GifPlainTextExtension
{
    /// <summary>
    /// Gets or sets the number of pixels from the left edge of the logical screen to the left edge
    /// of the text grid.
    /// </summary>
    public ushort Left { get; init; }

    /// <summary>
    /// Gets or sets the number of pixels from the top edge of the logical screen to the top edge of
    /// the text grid.
    /// </summary>
    public ushort Top { get; init; }

    /// <summary>
    /// Gets or sets the width of the text grid in pixels.
    /// </summary>
    public ushort Width { get; init; }

    /// <summary>
    /// Gets or sets the height of the text grid in pixels.
    /// </summary>
    public ushort Height { get; init; }

    /// <summary>
    /// Gets or sets the width of each character cell in pixels.
    /// </summary>
    public byte CellWidth { get; init; }

    /// <summary>
    /// Gets or sets the height of each character cell in pixels.
    /// </summary>
    public byte CellHeight { get; init; }

    /// <summary>
    /// Gets or sets the index of the color in the global color table to use for the text
    /// foreground.
    /// </summary>
    public byte ForegroundColorIndex { get; init; }

    /// <summary>
    /// Gets or sets the index of the color in the global color table to use for the text
    /// background.
    /// </summary>
    public byte BackgroundColorIndex { get; init; }
}
