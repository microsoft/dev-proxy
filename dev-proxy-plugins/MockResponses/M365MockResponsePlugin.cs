// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Web;
using Microsoft.DevProxy.Abstractions;

namespace Microsoft.DevProxy.Plugins.MockResponses;

class IdToken {
    [JsonPropertyName("aud")]
    public string? Aud { get; set; }
    [JsonPropertyName("iss")]
    public string? Iss { get; set; }
    [JsonPropertyName("iat")]
    public int? Iat { get; set; }
    [JsonPropertyName("nbf")]
    public int? Nbf { get; set; }
    [JsonPropertyName("exp")]
    public int? Exp { get; set; }
    [JsonPropertyName("name")]
    public string? Name { get; set; }
    [JsonPropertyName("nonce")]
    public string? Nonce { get; set; }
    [JsonPropertyName("oid")]
    public string? Oid { get; set; }
    [JsonPropertyName("preferred_username")]
    public string? PreferredUsername { get; set; }
    [JsonPropertyName("rh")]
    public string? Rh { get; set; }
    [JsonPropertyName("sub")]
    public string? Sub { get; set; }
    [JsonPropertyName("tid")]
    public string? Tid { get; set; }
    [JsonPropertyName("uti")]
    public string? Uti { get; set; }
    [JsonPropertyName("ver")]
    public string? Ver { get; set; }
}

public class M365MockResponsePlugin : GraphMockResponsePlugin
{
    private string? lastNonce;
    public override string Name => nameof(M365MockResponsePlugin);

    protected override void ProcessMockResponse(ref byte[] body, IList<MockResponseHeader> headers, ProxyRequestArgs e, MockResponse? matchingResponse)
    {
        base.ProcessMockResponse(ref body, headers, e, matchingResponse);

        var bodyString = Encoding.UTF8.GetString(body);
        var changed = false;

        StoreLastNonce(e);
        UpdateMsalState(ref bodyString, e, ref changed);
        UpdateIdToken(ref bodyString, e, ref changed);

        if (changed)
        {
            body = Encoding.UTF8.GetBytes(bodyString);
        }
    }

    private void StoreLastNonce(ProxyRequestArgs e)
    {
        if (e.Session.HttpClient.Request.RequestUri.Query.Contains("nonce="))
        {
            var queryString = HttpUtility.ParseQueryString(e.Session.HttpClient.Request.RequestUri.Query);
            lastNonce = queryString["nonce"];
        }
    }

    private void UpdateIdToken(ref string body, ProxyRequestArgs e, ref bool changed)
    {
        if (!body.Contains("id_token\":\"@dynamic") ||
            string.IsNullOrEmpty(lastNonce))
        {
            return;
        }

        var idTokenRegex = new Regex("id_token\":\"([^\"]+)\"");

        var idToken = idTokenRegex.Match(body).Groups[1].Value;
        idToken = idToken.Replace("@dynamic.", "");
        var tokenChunks = idToken.Split('.');
        // base64 decode the second chunk from the array
        // before decoding, we need to pad the base64 to a multiple of 4
        // or Convert.FromBase64String will throw an exception
        var decodedToken = Encoding.UTF8.GetString(Convert.FromBase64String(PadBase64(tokenChunks[1])));
        var token = JsonSerializer.Deserialize<IdToken>(decodedToken);
        if (token is null)
        {
            return;
        }
        
        token.Nonce = lastNonce;

        tokenChunks[1] = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(token)));
        body = idTokenRegex.Replace(body, $"id_token\":\"{string.Join('.', tokenChunks)}\"");
        changed = true;
    }

    private string PadBase64(string base64)
    {
        var padding = new string('=', (4 - base64.Length % 4) % 4);
        return base64 + padding;
    }

    private void UpdateMsalState(ref string body, ProxyRequestArgs e, ref bool changed)
    {
        if (!body.Contains("state=@dynamic") ||
          !e.Session.HttpClient.Request.RequestUri.Query.Contains("state="))
        {
            return;
        }

        var queryString = HttpUtility.ParseQueryString(e.Session.HttpClient.Request.RequestUri.Query);
        var msalState = queryString["state"];
        body = body.Replace("state=@dynamic", $"state={msalState}");
        changed = true;
    }
}