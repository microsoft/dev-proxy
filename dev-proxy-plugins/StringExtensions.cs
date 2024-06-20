// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace System;

internal static class StringExtensions
{
    internal static string MaxLength(this string input, int maxLength)
    {
        return input.Length <= maxLength ? input : input[..maxLength];
    }

    internal static string ToPascalCase(this string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        return char.ToUpper(input[0]) + input[1..];
    }
}