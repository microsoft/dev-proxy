// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
}
