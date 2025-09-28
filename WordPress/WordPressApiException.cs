using System.Net;

namespace WpAiCli.WordPress;

public sealed class WordPressApiException : Exception
{
    public WordPressApiException(HttpStatusCode statusCode, string responseBody)
        : base($"WordPress API が { (int)statusCode } ({statusCode}) を返しました。")
    {
        StatusCode = statusCode;
        ResponseBody = responseBody;
    }

    public HttpStatusCode StatusCode { get; }
    public string ResponseBody { get; }
}
