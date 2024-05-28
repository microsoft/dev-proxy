// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License

using Microsoft.DevProxy.Logging;

namespace System.IO;

public static class TextWriterExtensions
{
    const string _defaultForegroundColor = "\x1B[39m\x1B[22m";
    const string _defaultBackgroundColor = "\x1B[49m";

    public static void ResetColor(this TextWriter writer)
    {
        writer.Write(_defaultForegroundColor);
        writer.Write(_defaultBackgroundColor);
    }

    public static void WriteColoredMessage(this TextWriter textWriter, string message, ConsoleColor? background, ConsoleColor? foreground)
    {
        if (Console.IsOutputRedirected)
        {
            textWriter.Write(message);
            return;
        }
        
        // Order: backgroundcolor, foregroundcolor, Message, reset foregroundcolor, reset backgroundcolor
        if (background.HasValue)
        {
            textWriter.Write(AnsiParser.GetBackgroundColorEscapeCode(background.Value));
        }
        if (foreground.HasValue)
        {
            textWriter.Write(AnsiParser.GetForegroundColorEscapeCode(foreground.Value));
        }
        textWriter.Write(message);
        if (foreground.HasValue)
        {
            textWriter.Write(AnsiParser.DefaultForegroundColor); // reset to default foreground color
        }
        if (background.HasValue)
        {
            textWriter.Write(AnsiParser.DefaultBackgroundColor); // reset to the background color
        }
    }
}
