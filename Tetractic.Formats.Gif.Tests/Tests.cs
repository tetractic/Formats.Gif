// Copyright Carl Reinke
//
// This file is part of a library that is licensed under the terms of the GNU
// Lesser General Public License Version 3 as published by the Free Software
// Foundation.
//
// This license does not grant rights under trademark law for use of any trade
// names, trademarks, or service marks.

using System;
using System.IO;
using Xunit;

namespace Tetractic.Formats.Gif.Tests;

public static class Tests
{
    [Fact(Explicit = true)]
    public static void TestWriterReaderStates()
    {
        using var stream = new MemoryStream();

        using (var writer = new GifWriter(stream, leaveOpen: true))
        {
            writer.WriteHeader(GifVersion.Version89a);
            writer.WriteLogicalScreenDescriptor(new GifLogicalScreenDescriptor
            {
                HasGlobalColorTable = true,
            });
            writer.WriteColorTable([]);

            writer.WriteImageDescriptor(new GifImageDescriptor
            {
                HasLocalColorTable = true,
            });
            writer.WriteColorTable([]);
            writer.WriteImageData([]);

            writer.WriteExtensionLabel(GifExtensionLabel.Comment);
            writer.WriteSubblock([0x00]);
            writer.WriteBlockTerminator();

            writer.WriteGraphicControlExtension(default);
            writer.WriteBlockTerminator();

            writer.WritePlainTextExtension(default);
            writer.WriteBlockTerminator();

            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            writer.WriteApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            writer.WriteNetscapeApplicationExtensionSubblock(new NetscapeApplicationExtension.LoopingSubblock());
            writer.WriteBlockTerminator();

            writer.WriteTrailer();
        }

        stream.Position = 0;

        using (var reader = new GifReader(stream))
        {
            _ = reader.ReadHeader();
            _ = reader.ReadLogicalScreenDescriptor();
            _ = reader.ReadColorTable();

            _ = reader.Peek();
            _ = reader.ReadImageDescriptor();
            _ = reader.ReadColorTable();
            _ = reader.ReadImageData();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            _ = reader.ReadSubblock();
            _ = reader.ReadSubblock();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            _ = reader.ReadGraphicControlExtension();
            _ = reader.ReadSubblock();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            _ = reader.ReadPlainTextExtension();
            _ = reader.ReadSubblock();

            _ = reader.Peek();
            _ = reader.ReadExtensionLabel();
            Span<byte> applicationIdentifier = stackalloc byte[8];
            Span<byte> applicationAuthenticationCode = stackalloc byte[3];
            reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
            _ = reader.ReadNetscapeApplicationExtensionSubblock();
            _ = reader.ReadNetscapeApplicationExtensionSubblock();

            _ = reader.Peek();
        }
    }
}
