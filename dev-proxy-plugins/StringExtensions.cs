// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace System;

internal static class StringExtensions
{
    internal static string MaxLength(this string input, int maxLength)
    {
        return input.Length <= maxLength ? input : input[..maxLength];
    }
}