// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.DevProxy.Abstractions;

public class MockRequest
{
    public string Url { get; set; } = string.Empty;
    public string Method { get; set; } = "POST";
    public dynamic? Body { get; set; }
    public List<MockRequestHeader>? Headers { get; set; }
}

public class MockRequestHeader
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;

    public MockRequestHeader()
    {
    }

    public MockRequestHeader(string name, string value)
    {
        Name = name;
        Value = value;
    }
}