using JobTracker.Web.Models;

namespace JobTracker.Web.Data;

/// <summary>
/// Optional sample rows for screenshots and demos. Off by default so your real
/// tracker is never mixed with fake applications.
/// </summary>
public static class DemoData
{
    public static async Task SeedIfEmptyAsync(IJobRepository repository)
    {
        if ((await repository.GetAllAsync()).Count > 0)
        {
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.Today);

        JobApplication[] samples =
        [
            new()
            {
                Company = "Contoso Health",
                Title = "Senior .NET Developer",
                Location = "Remote",
                Status = JobStatus.Interview,
                AppliedDate = today.AddDays(-12),
                FollowUpDate = today.AddDays(2),
                Notes = "Panel interview covers Blazor and SQL."
            },
            new()
            {
                Company = "Fabrikam Logistics",
                Title = "Full Stack Engineer",
                Location = "Atlanta, GA",
                Status = JobStatus.PhoneScreen,
                AppliedDate = today.AddDays(-6),
                FollowUpDate = today.AddDays(-1),
                Notes = "Recruiter said to expect a call this week."
            },
            new()
            {
                Company = "Northwind Software",
                Title = "Backend Developer",
                Location = "Remote",
                Status = JobStatus.Applied,
                AppliedDate = today.AddDays(-3)
            },
            new()
            {
                Company = "Adventure Works",
                Title = "Cloud Engineer",
                Location = "Hybrid",
                Status = JobStatus.Wishlist,
                AppliedDate = today
            },
            new()
            {
                Company = "Tailspin Toys",
                Title = ".NET Contractor",
                Location = "Remote",
                Status = JobStatus.Rejected,
                AppliedDate = today.AddDays(-20)
            }
        ];

        foreach (var sample in samples)
        {
            await repository.AddAsync(sample);
        }
    }
}
