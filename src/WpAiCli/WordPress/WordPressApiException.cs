using System.Net;

namespace WpAiCli.WordPress;

public sealed class WordPressApiException : Exception
{
    public WordPressApiException(HttpStatusCode statusCode, string responseBody)
        : base($"WordPress API returned { (int)statusCode } ({statusCode}).")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}
