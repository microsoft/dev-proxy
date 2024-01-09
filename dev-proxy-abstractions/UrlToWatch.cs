using System.Text.RegularExpressions;

namespace Microsoft.DevProxy.Abstractions;

public class UrlToWatch
{
    public bool Exclude { get; }
    public Regex Url { get; }

    public UrlToWatch(Regex url, bool exclude = false)
    {
        Exclude = exclude;
        Url = url;
    }
}