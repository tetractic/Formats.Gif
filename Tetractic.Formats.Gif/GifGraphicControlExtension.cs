// Copyright Carl Reinke
//
// This file is part of a library that is licensed under the terms of the GNU
// Lesser General Public License Version 3 as published by the Free Software
// Foundation.
//
// This license does not grant rights under trademark law for use of any trade
// names, trademarks, or service marks.

using System;

namespace Tetractic.Formats.Gif;

/// <summary>
/// The first sub-block of a Graphic Control Extension, which describes how a graphic rendering
/// block should be processed.
/// </summary>
public readonly struct GifGraphicControlExtension
{
#pragma warning disable format
    private const byte _reserved            = 0b111_000_0_0;
    private const byte _disposalMethodMask  = 0b000_111_0_0;
    private const byte _userInputFlag       = 0b000_000_1_0;
    private const byte _hasTransparentColor = 0b000_000_0_1;
#pragma warning restore format

    internal byte PackedFields { get; init; }

    /// <summary>
    /// Gets or sets a value that indicates what operation is performed on the area of the logical
    /// screen where the graphic was displayed when it is done being displayed.
    /// </summary>
    /// <exception cref="ArgumentException" accessor="set">The property is being set, and
    ///     <paramref name="value"/> is invalid.</exception>
    public GifDisposalMethod DisposalMethod
    {
        readonly get => (GifDisposalMethod)((PackedFields & _disposalMethodMask) >> 2);
        init
        {
            if (value > (GifDisposalMethod)0b111)
                throw new ArgumentException("Invalid value.", nameof(value));

            const byte mask = _disposalMethodMask;
            PackedFields = (byte)((PackedFields & ~mask) | ((byte)value << 2));
        }
    }

    /// <summary>
    /// Gets or sets a value that indicates whether user input causes the graphic to be done being
    /// displayed.
    /// </summary>
    public bool UserInput
    {
        readonly get => (PackedFields & _userInputFlag) != 0;
        init
        {
            const byte flag = _userInputFlag;
            PackedFields = (byte)((PackedFields & ~flag) | (value ? flag : 0));
        }
    }

    /// <summary>
    /// Gets or sets a value indicating whether there is a color index that is transparent.
    /// </summary>
    public bool HasTransparentColor
    {
        readonly get => (PackedFields & _hasTransparentColor) != 0;
        init
        {
            const byte flag = _hasTransparentColor;
            PackedFields = (byte)((PackedFields & ~flag) | (value ? flag : 0));
        }
    }

    /// <summary>
    /// Gets or sets the number of centiseconds after which the graphic will be done being
    /// displayed.
    /// </summary>
    public ushort DelayTime { get; init; }

    /// <summary>
    /// Gets or sets the index of the color in the image that is transparent.
    /// </summary>
    /// <remarks>
    /// If <see cref="HasTransparentColor"/> is <see langword="false"/> then the value should be
    /// zero.
    /// </remarks>
    public byte TransparentColorIndex { get; init; }

    internal readonly bool Reserved => (PackedFields & _reserved) != 0;
}
