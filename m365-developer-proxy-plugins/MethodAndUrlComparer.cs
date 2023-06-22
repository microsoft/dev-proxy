using System.Diagnostics.CodeAnalysis;

namespace Microsoft365.DeveloperProxy.Plugins.RequestLogs;

internal class MethodAndUrlComparer : IEqualityComparer<Tuple<string, string>>
{
  public bool Equals(Tuple<string, string>? x, Tuple<string, string>? y)
  {
    if (object.ReferenceEquals(x, y))
    {
      return true;
    }

    if (object.ReferenceEquals(x, null) || object.ReferenceEquals(y, null))
    {
      return false;
    }

    return x.Item1 == y.Item1 && x.Item2 == y.Item2;
  }

  public int GetHashCode([DisallowNull] Tuple<string, string> obj)
  {
    if (obj == null)
    {
      return 0;
    }

    int methodHashCode = obj.Item1.GetHashCode();
    int urlHashCode = obj.Item2.GetHashCode();

    return methodHashCode ^ urlHashCode;
  }
}