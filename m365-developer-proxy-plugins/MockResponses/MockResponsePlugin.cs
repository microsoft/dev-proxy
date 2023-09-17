// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Configuration;
using Microsoft365.DeveloperProxy.Abstractions;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Titanium.Web.Proxy.EventArguments;
using Titanium.Web.Proxy.Http;
using Titanium.Web.Proxy.Models;

namespace Microsoft365.DeveloperProxy.Plugins.MockResponses;

public class MockResponseConfiguration {
    [JsonIgnore]
    public bool NoMocks { get; set; } = false;
    [JsonIgnore]
    public string MocksFile { get; set; } = "responses.json";
    public bool BlockUnmockedRequests { get; set; } = false;

    [JsonPropertyName("responses")]
    public IEnumerable<MockResponse> Responses { get; set; } = Array.Empty<MockResponse>();
}

public class MockResponsePlugin : BaseProxyPlugin {
    protected MockResponseConfiguration _configuration = new();
    private MockResponsesLoader? _loader = null;
    private readonly Option<bool?> _noMocks;
    private readonly Option<string?> _mocksFile;
    public override string Name => nameof(MockResponsePlugin);
    private IProxyConfiguration? _proxyConfiguration;
    // tracks the number of times a mock has been applied
    // used in combination with mocks that have an Nth property
    private Dictionary<string, int> _appliedMocks = new();

    public MockResponsePlugin() {
        _noMocks = new Option<bool?>("--no-mocks", "Disable loading mock requests");
        _noMocks.AddAlias("-n");
        _noMocks.ArgumentHelpName = "no mocks";

        _mocksFile = new Option<string?>("--mocks-file", "Provide a file populated with mock responses");
        _mocksFile.ArgumentHelpName= "mocks file";
    }

    public override void Register(IPluginEvents pluginEvents,
                            IProxyContext context,
                            ISet<UrlToWatch> urlsToWatch,
                            IConfigurationSection? configSection = null) {
        base.Register(pluginEvents, context, urlsToWatch, configSection);
        
        configSection?.Bind(_configuration);
        _loader = new MockResponsesLoader(_logger!, _configuration);

        pluginEvents.Init += OnInit;
        pluginEvents.OptionsLoaded += OnOptionsLoaded;
        pluginEvents.BeforeRequest += OnRequest;

        _proxyConfiguration = context.Configuration;
    }

    private void OnInit(object? sender, InitArgs e) {
        e.RootCommand.AddOption(_noMocks);
        e.RootCommand.AddOption(_mocksFile);
    }

    private void OnOptionsLoaded(object? sender, OptionsLoadedArgs e) {
        InvocationContext context = e.Context;

        // allow disabling of mocks as a command line option
        var noMocks = context.ParseResult.GetValueForOption(_noMocks);
        if (noMocks.HasValue) {
            _configuration.NoMocks = noMocks.Value;
        }
        if (_configuration.NoMocks) {
            // mocks have been disabled. No need to continue
            return;
        }

        // update the name of the mocks file to load from if supplied
        string? mocksFile = context.ParseResult.GetValueForOption(_mocksFile);
        if (mocksFile is not null) {
            _configuration.MocksFile = Path.GetFullPath(ProxyUtils.ReplacePathTokens(mocksFile));
        }

        _configuration.MocksFile = Path.GetFullPath(ProxyUtils.ReplacePathTokens(_configuration.MocksFile), Path.GetDirectoryName(_proxyConfiguration?.ConfigFile ?? string.Empty) ?? string.Empty);

        // load the responses from the configured mocks file
        _loader?.InitResponsesWatcher();
    }

    protected virtual async Task OnRequest(object? sender, ProxyRequestArgs e) {
        Request request = e.Session.HttpClient.Request;
        ResponseState state = e.ResponseState;
        if (!_configuration.NoMocks && _urlsToWatch is not null && e.ShouldExecute(_urlsToWatch)) {
            var matchingResponse = GetMatchingMockResponse(request);
            if (matchingResponse is not null) {
                ProcessMockResponse(e.Session, matchingResponse);
                state.HasBeenSet = true;
            }
            else if (_configuration.BlockUnmockedRequests) {
                ProcessMockResponse(e.Session, new MockResponse {
                    Method = request.Method,
                    Url = request.Url,
                    ResponseCode = 502,
                    ResponseBody = new GraphErrorResponseBody(new GraphErrorResponseError {
                        Code = "Bad Gateway",
                        Message = $"No mock response found for {request.Method} {request.Url}"
                    })
                });
                state.HasBeenSet = true;
            }
        }
    }

    private MockResponse? GetMatchingMockResponse(Request request) {
        if (_configuration.NoMocks ||
            _configuration.Responses is null ||
            !_configuration.Responses.Any()) {
            return null;
        }

        var mockResponse = _configuration.Responses.FirstOrDefault(mockResponse => {
            if (mockResponse.Method != request.Method) return false;
            if (mockResponse.Url == request.Url && IsNthRequest(mockResponse)) {
                return true;
            }

            // check if the URL contains a wildcard
            // if it doesn't, it's not a match for the current request for sure
            if (!mockResponse.Url.Contains('*')) {
                return false;
            }

            //turn mock URL with wildcard into a regex and match against the request URL
            var mockResponseUrlRegex = Regex.Escape(mockResponse.Url).Replace("\\*", ".*");
            return Regex.IsMatch(request.Url, $"^{mockResponseUrlRegex}$") && IsNthRequest(mockResponse);
        });
        
        if (mockResponse is not null)
        {
            if (!_appliedMocks.ContainsKey(mockResponse.Url))
            {
                _appliedMocks.Add(mockResponse.Url, 0);
            }
            _appliedMocks[mockResponse.Url]++;
        }

        return mockResponse;
    }

    private bool IsNthRequest(MockResponse mockResponse)
    {
        if (mockResponse.Nth is null)
        {
            // mock doesn't define an Nth property so it always qualifies
            return true;
        }

        var nth = 0;
        _appliedMocks.TryGetValue(mockResponse.Url, out nth);
        nth++;

        return mockResponse.Nth == nth;
    }

    private void ProcessMockResponse(SessionEventArgs e, MockResponse matchingResponse) {
        string? body = null;
        string requestId = Guid.NewGuid().ToString();
        string requestDate = DateTime.Now.ToString();
        var headers = ProxyUtils.BuildGraphResponseHeaders(e.HttpClient.Request, requestId, requestDate);
        HttpStatusCode statusCode = HttpStatusCode.OK;
        if (matchingResponse.ResponseCode is not null) {
            statusCode = (HttpStatusCode)matchingResponse.ResponseCode;
        }

        if (matchingResponse.ResponseHeaders is not null) {
            foreach (var key in matchingResponse.ResponseHeaders.Keys) {
                headers.Add(new HttpHeader(key, matchingResponse.ResponseHeaders[key]));
            }
        }
        // default the content type to application/json unless set in the mock response
        if (!headers.Any(h => h.Name.Equals("content-type", StringComparison.OrdinalIgnoreCase))) {
            headers.Add(new HttpHeader("content-type", "application/json"));
        }

        if (matchingResponse.ResponseBody is not null) {
            var bodyString = JsonSerializer.Serialize(matchingResponse.ResponseBody) as string;
            // we get a JSON string so need to start with the opening quote
            if (bodyString?.StartsWith("\"@") ?? false) {
                // we've got a mock body starting with @-token which means we're sending
                // a response from a file on disk
                // if we can read the file, we can immediately send the response and
                // skip the rest of the logic in this method
                // remove the surrounding quotes and the @-token
                var filePath = Path.Combine(Path.GetDirectoryName(_configuration.MocksFile) ?? "", ProxyUtils.ReplacePathTokens(bodyString.Trim('"').Substring(1)));
                if (!File.Exists(filePath)) {
                    _logger?.LogError($"File {filePath} not found. Serving file path in the mock response");
                    body = bodyString;
                }
                else {
                    var bodyBytes = File.ReadAllBytes(filePath);
                    e.GenericResponse(bodyBytes, statusCode, headers);
                }
            }
            else {
                body = bodyString;
            }
        }
        e.GenericResponse(body ?? string.Empty, statusCode, headers);

        _logger?.LogRequest(new[] { $"{matchingResponse.ResponseCode ?? 200} {matchingResponse.Url}" }, MessageType.Mocked, new LoggingContext(e));
    }
}
