using System.ComponentModel.DataAnnotations;

namespace JobTracker.Web.Models;

public class JobApplication
{
    public int Id { get; set; }

    [Required, StringLength(120)]
    public string Company { get; set; } = "";

    [Required, StringLength(120), Display(Name = "Job title")]
    public string Title { get; set; } = "";

    [StringLength(120)]
    public string? Location { get; set; }

    [Url, StringLength(500), Display(Name = "Posting URL")]
    public string? Url { get; set; }

    public JobStatus Status { get; set; } = JobStatus.Applied;

    [DataType(DataType.Date), Display(Name = "Date applied")]
    public DateOnly AppliedDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    [DataType(DataType.Date), Display(Name = "Follow up on")]
    public DateOnly? FollowUpDate { get; set; }

    [StringLength(2000)]
    public string? Notes { get; set; }

    /// <summary>
    /// True when a follow-up date has arrived and the application is still open
    /// (an offer or a rejection needs no follow-up).
    /// </summary>
    public bool IsFollowUpDue(DateOnly today) =>
        FollowUpDate is { } due
        && due <= today
        && Status is not (JobStatus.Offer or JobStatus.Rejected);
}
