// Copyright Carl Reinke
//
// This file is part of a library that is licensed under the terms of the GNU
// Lesser General Public License Version 3 as published by the Free Software
// Foundation.
//
// This license does not grant rights under trademark law for use of any trade
// names, trademarks, or service marks.

using System;
using System.Diagnostics;
using System.IO;
using Tetractic.CommandLine;

namespace Tetractic.Formats.Gif.Reencode;

internal static class Program
{
    public static int Main(string[] args)
    {
        var rootCommand = new RootCommand(typeof(Program).Namespace!);
        var inFileParameter = rootCommand.AddParameter("INFILE", "The input file.");
        var outFileParameter = rootCommand.AddParameter("OUTFILE", "The output file.");
        outFileParameter.Optional = true;
        rootCommand.VerboseOption = rootCommand.AddOption('v', null, "Enables additional output.");

        rootCommand.SetInvokeHandler(() =>
        {
            string inPath = inFileParameter.Value;
            string? outPath = outFileParameter.ValueOrDefault;
            string? tempPath = outPath is null
                ? null
                : Path.Combine(Path.GetDirectoryName(inPath) ?? "", Path.GetRandomFileName());
            bool verbose = rootCommand.VerboseOption.Count > 0;

            try
            {
                using (var inStream = new FileStream(inPath, FileMode.Open, FileAccess.Read))
                using (Stream outStream = tempPath is null
                    ? new MemoryStream()
                    : new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write))
                using (var reader = new GifReader(inStream))
                using (var writer = new GifWriter(outStream))
                {
                    while (true)
                    {
                        var part = reader.Peek();
                        switch (part)
                        {
                            case GifReader.ReadPart.Header:
                            {
                                var version = reader.ReadHeader();
                                if (verbose)
                                {
                                    Console.WriteLine($"Header");
                                    Console.WriteLine($"  Version: {((int)version / 26 + 87) % 100}{(char)((int)version % 26 + 'a')}");
                                }
                                writer.WriteHeader(version);
                                break;
                            }
                            case GifReader.ReadPart.LogicalScreenDescriptor:
                            {
                                var descriptor = reader.ReadLogicalScreenDescriptor();
                                if (verbose)
                                {
                                    Console.WriteLine($"Logical Screen Descriptor");
                                    Console.WriteLine($"  Width:              {descriptor.Width} pixels");
                                    Console.WriteLine($"  Height:             {descriptor.Height} pixels");
                                    Console.WriteLine($"  Has Global Colors:  {descriptor.HasGlobalColorTable}");
                                    Console.WriteLine($"  Color Resolution:   {descriptor.ColorResolution + 1} bits");
                                    Console.WriteLine($"  Sorted:             {descriptor.Sorted}");
                                    Console.WriteLine($"  Global Colors Size: {2 << descriptor.GlobalColorTableSize} colors");
                                    Console.WriteLine($"  Background Color #: {descriptor.BackgroundColorIndex}");
                                    Console.WriteLine($"  Pixel Aspect Ratio: {descriptor.PixelAspectRatio}");
                                }
                                writer.WriteLogicalScreenDescriptor(descriptor);
                                break;
                            }
                            case GifReader.ReadPart.GlobalColorTable:
                            {
                                var table = reader.ReadColorTable();
                                if (verbose)
                                {
                                    Console.WriteLine($"Global Color Table");
                                    Console.WriteLine("  (Omitted.)");
                                }
                                writer.WriteColorTable(table);
                                break;
                            }
                            case GifReader.ReadPart.ImageDescriptor:
                            {
                                var descriptor = reader.ReadImageDescriptor();
                                if (verbose)
                                {
                                    Console.WriteLine($"Image Descriptor");
                                    Console.WriteLine($"  Left:              {descriptor.Left}");
                                    Console.WriteLine($"  Top:               {descriptor.Top}");
                                    Console.WriteLine($"  Width:             {descriptor.Width}");
                                    Console.WriteLine($"  Height:            {descriptor.Height}");
                                    Console.WriteLine($"  Has Local Colors:  {descriptor.HasLocalColorTable}");
                                    Console.WriteLine($"  Interlaced:        {descriptor.Interlaced}");
                                    Console.WriteLine($"  Sorted:            {descriptor.Sorted}");
                                    Console.WriteLine($"  Local Colors Size: {2 << descriptor.LocalColorTableSize} colors");
                                }
                                writer.WriteImageDescriptor(descriptor);

                                if (reader.Peek() == GifReader.ReadPart.LocalColorTable)
                                {
                                    var table = reader.ReadColorTable();
                                    if (verbose)
                                    {
                                        Console.WriteLine($"Local Color Table");
                                        Console.WriteLine("  (Omitted.)");
                                    }
                                    writer.WriteColorTable(table);
                                }

                                byte[] data = reader.ReadImageData();
                                if (verbose)
                                {
                                    Console.WriteLine($"Image Data");
                                    Console.WriteLine("  (Omitted.)");
                                }
                                writer.WriteImageData(data);
                                break;
                            }
                            case GifReader.ReadPart.ExtensionLabel:
                            {
                                var label = reader.ReadExtensionLabel();
                                switch (label)
                                {
                                    case GifExtensionLabel.PlainText:
                                    {
                                        var extension = reader.ReadPlainTextExtension();
                                        if (verbose)
                                        {
                                            Console.WriteLine($"Plain Text Extension");
                                            Console.WriteLine($"  Left:               {extension.Left}");
                                            Console.WriteLine($"  Top:                {extension.Top}");
                                            Console.WriteLine($"  Width:              {extension.Width}");
                                            Console.WriteLine($"  Height:             {extension.Height}");
                                            Console.WriteLine($"  Cell Width:         {extension.CellWidth}");
                                            Console.WriteLine($"  Cell Height:        {extension.CellHeight}");
                                            Console.WriteLine($"  Foreground Color #: {extension.ForegroundColorIndex}");
                                            Console.WriteLine($"  Background Color #: {extension.BackgroundColorIndex}");
                                        }
                                        writer.WritePlainTextExtension(extension);

                                        ReadAndWriteSubblocks(reader, writer, verbose);
                                        break;
                                    }
                                    case GifExtensionLabel.GraphicControl:
                                    {
                                        var extension = reader.ReadGraphicControlExtension();
                                        if (verbose)
                                        {
                                            Console.WriteLine($"Graphic Control Extension");
                                            Console.WriteLine($"  Disposal Method:       {extension.DisposalMethod}");
                                            Console.WriteLine($"  User Input:            {extension.UserInput}");
                                            Console.WriteLine($"  Has Transparent Color: {extension.HasTransparentColor}");
                                            Console.WriteLine($"  Delay Time:            {extension.DelayTime} cs");
                                            Console.WriteLine($"  Transparent Color #:   {extension.TransparentColorIndex}");
                                        }
                                        writer.WriteGraphicControlExtension(extension);

                                        ReadAndWriteSubblocks(reader, writer, verbose);
                                        break;
                                    }
                                    case GifExtensionLabel.Comment:
                                    {
                                        if (verbose)
                                        {
                                            Console.WriteLine($"Comment Extension");
                                        }
                                        writer.WriteExtensionLabel(label);

                                        ReadAndWriteSubblocks(reader, writer, verbose);
                                        break;
                                    }
                                    case GifExtensionLabel.Application:
                                    {
                                        Span<byte> applicationIdentifier = stackalloc byte[8];
                                        Span<byte> applicationAuthenticationCode = stackalloc byte[3];
                                        reader.ReadApplicationExtension(applicationIdentifier, applicationAuthenticationCode);
                                        if (verbose)
                                        {
                                            Console.WriteLine($"Application Extension");
                                            Console.WriteLine($"  Application Identifier:");
                                            WriteHexLines(Console.Out, "    ", applicationIdentifier);
                                            Console.WriteLine($"  Application Authentication Code:");
                                            WriteHexLines(Console.Out, "    ", applicationAuthenticationCode);
                                        }
                                        writer.WriteApplicationExtension(applicationIdentifier, applicationAuthenticationCode);

                                        if (applicationIdentifier.SequenceEqual(NetscapeApplicationExtension.ApplicationIdentifier) &&
                                            applicationAuthenticationCode.SequenceEqual(NetscapeApplicationExtension.ApplicationAuthenticationCode))
                                        {
                                            while (true)
                                            {
                                                var subblock = reader.ReadNetscapeApplicationExtensionSubblock();
                                                if (subblock is null)
                                                {
                                                    if (verbose)
                                                    {
                                                        Console.WriteLine($"  Block Terminator");
                                                    }
                                                    writer.WriteBlockTerminator();
                                                    break;
                                                }

                                                if (verbose)
                                                {
                                                    switch (subblock)
                                                    {
                                                        case NetscapeApplicationExtension.LoopingSubblock loopingSubblock:
                                                        {
                                                            Console.WriteLine($"  Looping Sub-block");
                                                            Console.WriteLine($"    Loop Count: {loopingSubblock.LoopCount}");
                                                            break;
                                                        }
                                                        case NetscapeApplicationExtension.BufferingSubblock bufferingSubblock:
                                                        {
                                                            Console.WriteLine($"  Buffering Sub-block");
                                                            Console.WriteLine($"    Buffer Length: {bufferingSubblock.BufferLength}");
                                                            break;
                                                        }
                                                        default:
                                                            throw new UnreachableException();
                                                    }
                                                }
                                                writer.WriteNetscapeApplicationExtensionSubblock(subblock);
                                            }
                                        }
                                        else
                                        {
                                            ReadAndWriteSubblocks(reader, writer, verbose);
                                        }
                                        break;
                                    }
                                    default:
                                    {
                                        if (verbose)
                                        {
                                            Console.WriteLine($"Extension {label}");
                                        }
                                        writer.WriteExtensionLabel(label);

                                        ReadAndWriteSubblocks(reader, writer, verbose);
                                        break;
                                    }
                                }
                                break;
                            }
                            case GifReader.ReadPart.Trailer:
                            {
                                if (verbose)
                                {
                                    Console.WriteLine($"Trailer");
                                }
                                writer.WriteTrailer();

                                if (verbose)
                                {
                                    Console.WriteLine($"Input  size: {inStream.Length} bytes");
                                    Console.WriteLine($"Output size: {outStream.Length} bytes");
                                }
                                goto done;
                            }

                            case GifReader.ReadPart.LocalColorTable:
                            case GifReader.ReadPart.ImageData:
                            case GifReader.ReadPart.Subblock:
                            default:
                                throw new UnreachableException();
                        }
                    }
                }

            done:
                if (tempPath is not null)
                    File.Move(tempPath, outPath!);

                return 0;
            }
            catch
            {
                if (tempPath is not null)
                    File.Delete(tempPath);

                throw;
            }

            static void ReadAndWriteSubblocks(GifReader reader, GifWriter writer, bool verbose)
            {
                while (true)
                {
                    byte[]? subblock = reader.ReadSubblock();
                    if (subblock is null)
                    {
                        if (verbose)
                        {
                            Console.WriteLine($"  Block Terminator");
                        }
                        writer.WriteBlockTerminator();
                        break;
                    }

                    if (verbose)
                    {
                        Console.WriteLine($"  Subblock");
                        WriteHexLines(Console.Out, "    ", subblock);
                    }
                    writer.WriteSubblock(subblock);
                }
            }
        });

        try
        {
            return rootCommand.Execute(args);
        }
        catch (InvalidCommandLineException ex)
        {
            Console.Error.WriteLine(ex.Message);
            CommandHelp.WriteHelpHint(ex.Command, Console.Error);
            return -1;
        }
        catch (Exception ex)
        {
#if DEBUG
            Console.Error.WriteLine(ex);
#else
            Console.Error.WriteLine(ex.Message);
#endif
            return -1;
        }
    }

    private static void WriteHexLines(TextWriter writer, string prefix, ReadOnlySpan<byte> bytes)
    {
        while (bytes.Length > 0)
        {
            int length = int.Min(bytes.Length, 16);

            writer.Write(prefix);
            int j;
            for (j = 0; j < length; ++j)
                writer.Write($"{bytes[j]:X2} ");
            for (; j < 16; ++j)
                writer.Write("   ");
            for (j = 0; j < length; ++j)
                writer.Write(ToPrintable(bytes[j]));
            writer.WriteLine();

            bytes = bytes.Slice(length);
        }

        static char ToPrintable(byte b) => b >= ' ' && b <= '~' ? (char)b : '.';
    }
}
