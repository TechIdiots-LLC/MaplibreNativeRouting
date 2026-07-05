namespace MaplibreNative.Routing.Core.Routing;

public class RoutingException : Exception
{
    public RoutingException(string message) : base(message) { }
    public RoutingException(string message, Exception inner) : base(message, inner) { }
}
