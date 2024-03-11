// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.DevProxy.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Titanium.Web.Proxy.EventArguments;

namespace Microsoft.DevProxy.Plugins.Mocks;

public class GraphConnectorNotificationConfiguration : MockRequestConfiguration
{
    [JsonPropertyName("audience")]
    public string? Audience { get; set; }
    [JsonPropertyName("tenant")]
    public string? Tenant { get; set; }
}

public class GraphConnectorNotificationPlugin : MockRequestPlugin
{
    private string? _ticket = null;
    private IProxyContext? _context;
    private GraphConnectorNotificationConfiguration _graphConnectorConfiguration = new();

    public override string Name => nameof(GraphConnectorNotificationPlugin);

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null)
    {
        _context = context;

        base.Register(pluginEvents, context, urlsToWatch, configSection);
        configSection?.Bind(_graphConnectorConfiguration);
        _graphConnectorConfiguration.MockFile = _configuration.MockFile;
        _graphConnectorConfiguration.Request = _configuration.Request;
        
        pluginEvents.BeforeRequest += OnBeforeRequest;
    }

    private Task OnBeforeRequest(object sender, ProxyRequestArgs e)
    {
        if (!ProxyUtils.IsGraphRequest(e.Session.HttpClient.Request))
        {
            return Task.CompletedTask;
        }

        VerifyTicket(e.Session);
        return Task.CompletedTask;
    }

    private void VerifyTicket(SessionEventArgs session)
    {
        if (_ticket is null)
        {
            return;
        }

        var request = session.HttpClient.Request;

        if (request.Method != "POST" && request.Method != "DELETE")
        {
            return;
        }

        if ((request.Method == "POST" &&
            !request.RequestUri.AbsolutePath.EndsWith("/external/connections", StringComparison.OrdinalIgnoreCase)) ||
            (request.Method == "DELETE" &&
            !request.RequestUri.AbsolutePath.Contains("/external/connections/", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var ticketFromHeader = request.Headers.FirstOrDefault(h => h.Name.Equals("GraphConnectors-Ticket", StringComparison.OrdinalIgnoreCase))?.Value;
        if (ticketFromHeader is null)
        {
            _logger?.LogRequest(["No ticket header found in the Graph connector notification"], MessageType.Failed, new LoggingContext(session));
            return;
        }

        if (ticketFromHeader != _ticket)
        {
            _logger?.LogRequest([$"Ticket on the request does not match the expected ticket. Expected: {_ticket}. Request: {ticketFromHeader}"], MessageType.Failed, new LoggingContext(session));
        }
    }

    protected override async Task OnMockRequest(object sender, EventArgs e)
    {
        if (_configuration.Request is null)
        {
            _logger?.LogDebug("No mock request is configured. Skipping.");
            return;
        }

        using (var httpClient = new HttpClient())
        {
            var requestMessage = GetRequestMessage();
            if (requestMessage.Content is null)
            {
                _logger?.LogError("No body found in the mock request. Skipping.");
                return;
            }
            var requestBody = await requestMessage.Content.ReadAsStringAsync();
            requestBody = requestBody.Replace("@dynamic.validationToken", GetJwtToken());
            requestMessage.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
            LoadTicket();

            try
            {
                _logger?.LogRequest(["Sending Graph connector notification"], MessageType.Mocked, _configuration.Request.Method, _configuration.Request.Url);

                var response = await httpClient.SendAsync(requestMessage);

                if (response.StatusCode != HttpStatusCode.Accepted)
                {
                    _logger?.LogRequest([$"Incorrect response status code {(int)response.StatusCode} {response.StatusCode}. Expected: 202 Accepted"], MessageType.Failed, _configuration.Request.Method, _configuration.Request.Url);
                }

                if (response.Content is not null)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(content))
                    {
                        _logger?.LogRequest(["Received response body while empty response expected"], MessageType.Failed, _configuration.Request.Method, _configuration.Request.Url);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "An error has occurred while sending the Graph connector notification to {url}", _configuration.Request.Url);
            }
        }
    }

    private string GetJwtToken()
    {
        var signingCredentials = new X509SigningCredentials(_context?.Certificate, SecurityAlgorithms.RsaSha256);

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Claims = new Dictionary<string, object>
            {
                { "scp", "user_impersonation" },
                { "sub", "l3_roISQU222bULS9yi2k0XpqpOiMz5H3ZACo1GeXA" },
                // Graph Connector Service
                { "appid", "56c1da01-2129-48f7-9355-af6d59d42766" }
            },
            Expires = DateTime.UtcNow.AddMinutes(60),
            Issuer = $"https://sts.windows.net/{_graphConnectorConfiguration.Tenant}/",
            Audience = _graphConnectorConfiguration.Audience,
            SigningCredentials = signingCredentials
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    private void LoadTicket()
    {
        if (_ticket is not null)
        {
            return;
        }

        if (_configuration.Request?.Body is null)
        {
            _logger?.LogWarning("No body found in the Graph connector notification. Ticket will not be loaded.");
            return;
        }

        try
        {
            var body = (JsonElement)_configuration.Request.Body;
            _ticket = body.Get("value")?.Get(0)?.Get("resourceData")?.Get("connectorsTicket")?.GetString();

            if (string.IsNullOrEmpty(_ticket))
            {
                _logger?.LogError("No ticket found in the Graph connector notification body");
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "An error has occurred while reading the ticket from the Graph connector notification body");
        }
    }
}
