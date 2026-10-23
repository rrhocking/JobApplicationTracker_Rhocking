namespace JobTracker.Web.Models;

public sealed record JobListViewModel(
    IReadOnlyList<JobApplication> Jobs,
    JobStatus? Status,
    string? Search);
