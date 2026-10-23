using JobTracker.Web.Models;

namespace JobTracker.Web.Data;

/// <summary>
/// Everything the app needs from storage. Controllers and Blazor components depend on this
/// interface, never on SQLite, so the data layer can be swapped or faked in tests.
/// </summary>
public interface IJobRepository
{
    /// <summary>Newest applications first, optionally filtered by status and by company/title text.</summary>
    Task<IReadOnlyList<JobApplication>> GetAllAsync(
        JobStatus? status = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<JobApplication?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Inserts the job, sets <see cref="JobApplication.Id"/> on it, and returns the new id.</summary>
    Task<int> AddAsync(JobApplication job, CancellationToken cancellationToken = default);

    /// <returns>False when no job with that id exists.</returns>
    Task<bool> UpdateAsync(JobApplication job, CancellationToken cancellationToken = default);

    /// <returns>False when no job with that id exists.</returns>
    Task<bool> UpdateStatusAsync(int id, JobStatus status, CancellationToken cancellationToken = default);

    /// <returns>False when no job with that id exists.</returns>
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
}
