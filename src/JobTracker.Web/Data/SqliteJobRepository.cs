using System.Data.Common;
using System.Globalization;
using System.Text;
using JobTracker.Web.Models;
using Microsoft.Data.Sqlite;

namespace JobTracker.Web.Data;

/// <summary>
/// Plain ADO.NET against SQLite: hand-written SQL, every value passed as a parameter.
/// Each call opens its own short-lived connection (Microsoft.Data.Sqlite pools them),
/// so the class holds no state and can be shared freely.
/// </summary>
public sealed class SqliteJobRepository : IJobRepository
{
    // Dates are stored as ISO text ("2026-10-22"), which sorts correctly as plain text.
    private const string DateFormat = "yyyy-MM-dd";

    private const string SelectColumns =
        "SELECT Id, Company, Title, Location, Url, Status, AppliedDate, FollowUpDate, Notes FROM JobApplications";

    private readonly string _connectionString;

    public SqliteJobRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>Creates the table and index if they do not exist yet. Safe to call on every start.</summary>
    public void EnsureCreated()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS JobApplications (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Company      TEXT NOT NULL,
                Title        TEXT NOT NULL,
                Location     TEXT NULL,
                Url          TEXT NULL,
                Status       TEXT NOT NULL,
                AppliedDate  TEXT NOT NULL,
                FollowUpDate TEXT NULL,
                Notes        TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS IX_JobApplications_Status ON JobApplications (Status);
            """;
        command.ExecuteNonQuery();
    }

    public async Task<IReadOnlyList<JobApplication>> GetAllAsync(
        JobStatus? status = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var sql = new StringBuilder(SelectColumns);
        var filters = new List<string>();

        if (status is { } selectedStatus)
        {
            filters.Add("Status = $status");
            command.Parameters.AddWithValue("$status", selectedStatus.ToString());
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add("(Company LIKE $search ESCAPE '\\' OR Title LIKE $search ESCAPE '\\')");
            command.Parameters.AddWithValue("$search", "%" + EscapeLike(search.Trim()) + "%");
        }

        if (filters.Count > 0)
        {
            sql.Append(" WHERE ").Append(string.Join(" AND ", filters));
        }

        sql.Append(" ORDER BY AppliedDate DESC, Id DESC");
        command.CommandText = sql.ToString();

        var results = new List<JobApplication>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(Map(reader));
        }

        return results;
    }

    public async Task<JobApplication?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = SelectColumns + " WHERE Id = $id";
        command.Parameters.AddWithValue("$id", id);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<int> AddAsync(JobApplication job, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO JobApplications
                (Company, Title, Location, Url, Status, AppliedDate, FollowUpDate, Notes)
            VALUES
                ($company, $title, $location, $url, $status, $appliedDate, $followUpDate, $notes)
            RETURNING Id;
            """;
        AddJobParameters(command, job);

        var id = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        job.Id = id;
        return id;
    }

    public async Task<bool> UpdateAsync(JobApplication job, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            UPDATE JobApplications
            SET Company      = $company,
                Title        = $title,
                Location     = $location,
                Url          = $url,
                Status       = $status,
                AppliedDate  = $appliedDate,
                FollowUpDate = $followUpDate,
                Notes        = $notes
            WHERE Id = $id;
            """;
        AddJobParameters(command, job);
        command.Parameters.AddWithValue("$id", job.Id);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> UpdateStatusAsync(int id, JobStatus status, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = "UPDATE JobApplications SET Status = $status WHERE Id = $id;";
        command.Parameters.AddWithValue("$status", status.ToString());
        command.Parameters.AddWithValue("$id", id);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM JobApplications WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    // Column order here must match SelectColumns.
    private static JobApplication Map(DbDataReader reader) => new()
    {
        Id = reader.GetInt32(0),
        Company = reader.GetString(1),
        Title = reader.GetString(2),
        Location = reader.IsDBNull(3) ? null : reader.GetString(3),
        Url = reader.IsDBNull(4) ? null : reader.GetString(4),
        Status = Enum.Parse<JobStatus>(reader.GetString(5)),
        AppliedDate = ParseDate(reader.GetString(6)),
        FollowUpDate = reader.IsDBNull(7) ? null : ParseDate(reader.GetString(7)),
        Notes = reader.IsDBNull(8) ? null : reader.GetString(8)
    };

    private static void AddJobParameters(SqliteCommand command, JobApplication job)
    {
        command.Parameters.AddWithValue("$company", job.Company.Trim());
        command.Parameters.AddWithValue("$title", job.Title.Trim());
        command.Parameters.AddWithValue("$location", ToDbText(job.Location));
        command.Parameters.AddWithValue("$url", ToDbText(job.Url));
        command.Parameters.AddWithValue("$status", job.Status.ToString());
        command.Parameters.AddWithValue("$appliedDate", FormatDate(job.AppliedDate));
        command.Parameters.AddWithValue("$followUpDate", ToDbDate(job.FollowUpDate));
        command.Parameters.AddWithValue("$notes", ToDbText(job.Notes));
    }

    private static object ToDbText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? DBNull.Value : (object)trimmed;
    }

    private static object ToDbDate(DateOnly? value) =>
        value is { } date ? FormatDate(date) : DBNull.Value;

    private static string FormatDate(DateOnly value) =>
        value.ToString(DateFormat, CultureInfo.InvariantCulture);

    private static DateOnly ParseDate(string value) =>
        DateOnly.ParseExact(value, DateFormat, CultureInfo.InvariantCulture);

    // Without this, a search for "100%" or "a_b" would treat % and _ as wildcards.
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
}
