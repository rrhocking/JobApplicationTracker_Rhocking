namespace JobTracker.Web.Models;

/// <summary>What the friendly error page shows. Never contains exception details.</summary>
public record ErrorViewModel(int StatusCode, string Title, string Message, string RequestId)
{
    public static ErrorViewModel For(int statusCode, string requestId) => statusCode switch
    {
        404 => new ErrorViewModel(
            404,
            "Page not found",
            "We couldn't find the page you were looking for. The link may be wrong, or the application may have been deleted.",
            requestId),

        >= 500 => new ErrorViewModel(
            statusCode,
            "Something went wrong",
            "Sorry, that's our mistake. Please try again in a moment.",
            requestId),

        _ => new ErrorViewModel(
            statusCode,
            "We couldn't complete that request",
            "Please go back and try again.",
            requestId)
    };
}
