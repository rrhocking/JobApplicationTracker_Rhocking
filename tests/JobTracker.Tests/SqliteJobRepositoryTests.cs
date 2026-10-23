using JobTracker.Web.Data;
using JobTracker.Web.Models;
using Microsoft.Data.Sqlite;

namespace JobTracker.Tests;

/// <summary>
/// Runs the real SQL against a throwaway SQLite file, so these tests catch typos in the queries,
/// not just logic errors. Each test gets its own database file.
/// </summary>
public sealed class SqliteJobRepositoryTests : IDisposable
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"jobtracker-tests-{Guid.NewGuid():N}.db");

    private readonly SqliteJobRepository _repository;

    public SqliteJobRepositoryTests()
    {
        // Pooling is off so the file is released as soon as each connection closes.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Pooling = false
        }.ToString();

        _repository = new SqliteJobRepository(connectionString);
        _repository.EnsureCreated();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
        {
            File.Delete(_databasePath);
        }
    }

    private static JobApplication NewJob(
        string company = "Acme",
        string title = "Developer",
        JobStatus status = JobStatus.Applied,
        int daysAgo = 0) => new()
    {
        Company = company,
        Title = title,
        Status = status,
        AppliedDate = new DateOnly(2026, 10, 22).AddDays(-daysAgo)
    };

    [Fact]
    public async Task AddAsync_assigns_an_id_and_round_trips_every_field()
    {
        var job = new JobApplication
        {
            Company = "  Contoso  ",
            Title = "Engineer",
            Location = "Remote",
            Url = "https://example.com/jobs/1",
            Status = JobStatus.PhoneScreen,
            AppliedDate = new DateOnly(2026, 10, 1),
            FollowUpDate = new DateOnly(2026, 10, 8),
            Notes = "Ask about Blazor."
        };

        var id = await _repository.AddAsync(job);
        var saved = await _repository.GetByIdAsync(id);

        Assert.True(id > 0);
        Assert.Equal(id, job.Id);
        Assert.NotNull(saved);
        Assert.Equal(id, saved.Id);
        Assert.Equal("Contoso", saved.Company);
        Assert.Equal("Engineer", saved.Title);
        Assert.Equal("Remote", saved.Location);
        Assert.Equal("https://example.com/jobs/1", saved.Url);
        Assert.Equal(JobStatus.PhoneScreen, saved.Status);
        Assert.Equal(new DateOnly(2026, 10, 1), saved.AppliedDate);
        Assert.Equal(new DateOnly(2026, 10, 8), saved.FollowUpDate);
        Assert.Equal("Ask about Blazor.", saved.Notes);
    }

    [Fact]
    public async Task Optional_fields_come_back_as_null_when_blank()
    {
        var job = NewJob();
        job.Location = "   ";
        job.Url = null;
        job.FollowUpDate = null;
        job.Notes = "";

        var id = await _repository.AddAsync(job);
        var saved = await _repository.GetByIdAsync(id);

        Assert.NotNull(saved);
        Assert.Null(saved.Location);
        Assert.Null(saved.Url);
        Assert.Null(saved.FollowUpDate);
        Assert.Null(saved.Notes);
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_an_unknown_id()
    {
        Assert.Null(await _repository.GetByIdAsync(12345));
    }

    [Fact]
    public async Task GetAllAsync_lists_the_most_recently_applied_first()
    {
        await _repository.AddAsync(NewJob(company: "Oldest", daysAgo: 10));
        await _repository.AddAsync(NewJob(company: "Newest", daysAgo: 0));
        await _repository.AddAsync(NewJob(company: "Middle", daysAgo: 5));

        var all = await _repository.GetAllAsync();

        Assert.Equal(new[] { "Newest", "Middle", "Oldest" }, all.Select(j => j.Company));
    }

    [Fact]
    public async Task GetAllAsync_filters_by_status()
    {
        await _repository.AddAsync(NewJob(company: "A", status: JobStatus.Applied));
        await _repository.AddAsync(NewJob(company: "B", status: JobStatus.Interview));
        await _repository.AddAsync(NewJob(company: "C", status: JobStatus.Interview));

        var interviews = await _repository.GetAllAsync(status: JobStatus.Interview);

        Assert.Equal(2, interviews.Count);
        Assert.All(interviews, job => Assert.Equal(JobStatus.Interview, job.Status));
    }

    [Fact]
    public async Task GetAllAsync_searches_company_and_title_ignoring_case()
    {
        await _repository.AddAsync(NewJob(company: "Contoso", title: "Backend Developer"));
        await _repository.AddAsync(NewJob(company: "Fabrikam", title: "Cloud Engineer"));

        var byCompany = await _repository.GetAllAsync(search: "contoso");
        var byTitle = await _repository.GetAllAsync(search: "ENGINEER");

        Assert.Equal("Contoso", Assert.Single(byCompany).Company);
        Assert.Equal("Fabrikam", Assert.Single(byTitle).Company);
    }

    [Fact]
    public async Task GetAllAsync_combines_status_and_search()
    {
        await _repository.AddAsync(NewJob(company: "Contoso", status: JobStatus.Applied));
        await _repository.AddAsync(NewJob(company: "Contoso Labs", status: JobStatus.Offer));

        var results = await _repository.GetAllAsync(status: JobStatus.Offer, search: "contoso");

        Assert.Equal("Contoso Labs", Assert.Single(results).Company);
    }

    [Fact]
    public async Task GetAllAsync_treats_percent_and_underscore_in_a_search_literally()
    {
        await _repository.AddAsync(NewJob(company: "Acme_Corp"));
        await _repository.AddAsync(NewJob(company: "AcmeXCorp"));
        await _repository.AddAsync(NewJob(company: "100% Remote Inc"));
        await _repository.AddAsync(NewJob(company: "1000 Remote Inc"));

        var underscore = await _repository.GetAllAsync(search: "Acme_");
        var percent = await _repository.GetAllAsync(search: "100%");

        Assert.Equal("Acme_Corp", Assert.Single(underscore).Company);
        Assert.Equal("100% Remote Inc", Assert.Single(percent).Company);
    }

    [Fact]
    public async Task Quotes_and_sql_in_user_text_are_stored_as_plain_text()
    {
        const string nasty = "O'Brien & Sons\"; DROP TABLE JobApplications;--";

        var id = await _repository.AddAsync(NewJob(company: nasty));
        var saved = await _repository.GetByIdAsync(id);
        var all = await _repository.GetAllAsync(search: "DROP TABLE");

        Assert.NotNull(saved);
        Assert.Equal(nasty, saved.Company);
        Assert.Single(all);
    }

    [Fact]
    public async Task UpdateAsync_changes_the_stored_values()
    {
        var id = await _repository.AddAsync(NewJob(company: "Before"));
        var job = await _repository.GetByIdAsync(id);
        Assert.NotNull(job);

        job.Company = "After";
        job.Status = JobStatus.Offer;
        job.FollowUpDate = new DateOnly(2026, 11, 1);
        job.Notes = "Negotiate.";

        Assert.True(await _repository.UpdateAsync(job));

        var saved = await _repository.GetByIdAsync(id);
        Assert.NotNull(saved);
        Assert.Equal("After", saved.Company);
        Assert.Equal(JobStatus.Offer, saved.Status);
        Assert.Equal(new DateOnly(2026, 11, 1), saved.FollowUpDate);
        Assert.Equal("Negotiate.", saved.Notes);
    }

    [Fact]
    public async Task UpdateAsync_returns_false_for_an_unknown_id()
    {
        var job = NewJob();
        job.Id = 999;

        Assert.False(await _repository.UpdateAsync(job));
    }

    [Fact]
    public async Task UpdateStatusAsync_changes_only_the_status()
    {
        var id = await _repository.AddAsync(NewJob(company: "Keep Me", status: JobStatus.Applied));

        Assert.True(await _repository.UpdateStatusAsync(id, JobStatus.Interview));

        var saved = await _repository.GetByIdAsync(id);
        Assert.NotNull(saved);
        Assert.Equal(JobStatus.Interview, saved.Status);
        Assert.Equal("Keep Me", saved.Company);
        Assert.False(await _repository.UpdateStatusAsync(999, JobStatus.Offer));
    }

    [Fact]
    public async Task DeleteAsync_removes_the_job_once()
    {
        var id = await _repository.AddAsync(NewJob());

        Assert.True(await _repository.DeleteAsync(id));
        Assert.Null(await _repository.GetByIdAsync(id));
        Assert.False(await _repository.DeleteAsync(id));
    }

    [Fact]
    public void IsFollowUpDue_is_true_only_for_open_applications_on_or_past_the_date()
    {
        var today = new DateOnly(2026, 10, 22);

        var due = new JobApplication { Status = JobStatus.Applied, FollowUpDate = today };
        var later = new JobApplication { Status = JobStatus.Applied, FollowUpDate = today.AddDays(1) };
        var none = new JobApplication { Status = JobStatus.Applied, FollowUpDate = null };
        var offer = new JobApplication { Status = JobStatus.Offer, FollowUpDate = today.AddDays(-3) };
        var rejected = new JobApplication { Status = JobStatus.Rejected, FollowUpDate = today.AddDays(-3) };

        Assert.True(due.IsFollowUpDue(today));
        Assert.False(later.IsFollowUpDue(today));
        Assert.False(none.IsFollowUpDue(today));
        Assert.False(offer.IsFollowUpDue(today));
        Assert.False(rejected.IsFollowUpDue(today));
    }
}
