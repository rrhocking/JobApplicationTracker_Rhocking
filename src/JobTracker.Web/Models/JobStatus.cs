namespace JobTracker.Web.Models;

/// <summary>
/// Where an application stands. The order matters: the board shows these left to right,
/// and Rejected sits outside the normal Wishlist-to-Offer flow.
/// </summary>
public enum JobStatus
{
    Wishlist,
    Applied,
    PhoneScreen,
    Interview,
    Offer,
    Rejected
}

public static class JobStatusExtensions
{
    public static string ToLabel(this JobStatus status) => status switch
    {
        JobStatus.PhoneScreen => "Phone screen",
        _ => status.ToString()
    };

    public static string ToCssClass(this JobStatus status) => "status-" + status.ToString().ToLowerInvariant();
}
