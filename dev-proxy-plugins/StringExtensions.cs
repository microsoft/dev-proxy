// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#pragma warning disable IDE0130
namespace System;
#pragma warning restore IDE0130

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

    internal static string Replace(this string input, string oldValue, string newValue, int startIndex)
    {
        if (string.IsNullOrEmpty(input) || string.IsNullOrEmpty(oldValue))
        {
            return input;
        }

        if (startIndex < 0 || startIndex >= input.Length)
        {
            return input;
        }

        return input[..startIndex] + input[startIndex..].Replace(oldValue, newValue);
    }
}