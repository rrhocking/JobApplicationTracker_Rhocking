using System.Data;
using System.Data.Common;
using System.Text;
using JobTracker.Web.Models;
using Microsoft.Data.SqlClient;

namespace JobTracker.Web.Data;

/// <summary>
/// Plain ADO.NET against SQL Server / Azure SQL Database: the same idea as SqliteJobRepository
/// (hand-written SQL, every value passed as a parameter), written in T-SQL.
/// Each call opens its own short-lived connection (SqlClient pools them), so the class holds no state.
/// </summary>
public sealed class SqlServerJobRepository : IJobRepository
{
    private const string SelectColumns =
        "SELECT Id, Company, Title, Location, Url, Status, AppliedDate, FollowUpDate, Notes FROM dbo.JobApplications";

    // The free Azure SQL database pauses itself when idle. The first connection after a pause is
    // refused while it wakes up (error 40613), so these "try again" errors are retried for a while.
    private static readonly HashSet<int> RetryableErrors =
        [40613, 40197, 40501, 49918, 49919, 49920, 4060, 10928, 10929, 10060, 10054, 233, 64];

    private const int MaxAttempts = 10;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);

    private readonly string _connectionString;

    public SqlServerJobRepository(string connectionString)
    {
        _connectionString = connectionString;
    }

    /// <summary>Creates the table and index if they do not exist yet. Safe to call on every start.</summary>
    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            IF OBJECT_ID(N'dbo.JobApplications', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.JobApplications (
                    Id           INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_JobApplications PRIMARY KEY,
                    Company      NVARCHAR(120)  NOT NULL,
                    Title        NVARCHAR(120)  NOT NULL,
                    Location     NVARCHAR(120)  NULL,
                    Url          NVARCHAR(500)  NULL,
                    Status       NVARCHAR(30)   NOT NULL,
                    AppliedDate  DATE           NOT NULL,
                    FollowUpDate DATE           NULL,
                    Notes        NVARCHAR(2000) NULL
                );
                CREATE INDEX IX_JobApplications_Status ON dbo.JobApplications (Status);
            END
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
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
            filters.Add("Status = @status");
            AddText(command, "@status", selectedStatus.ToString(), 30);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            filters.Add("(Company LIKE @search ESCAPE '\\' OR Title LIKE @search ESCAPE '\\')");
            AddText(command, "@search", "%" + EscapeLike(search.Trim()) + "%", 300);
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

        command.CommandText = SelectColumns + " WHERE Id = @id";
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Map(reader) : null;
    }

    public async Task<int> AddAsync(JobApplication job, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // OUTPUT INSERTED.Id is the T-SQL way to get the new identity value back (SQLite used RETURNING).
        command.CommandText = """
            INSERT INTO dbo.JobApplications
                (Company, Title, Location, Url, Status, AppliedDate, FollowUpDate, Notes)
            OUTPUT INSERTED.Id
            VALUES
                (@company, @title, @location, @url, @status, @appliedDate, @followUpDate, @notes);
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
            UPDATE dbo.JobApplications
            SET Company      = @company,
                Title        = @title,
                Location     = @location,
                Url          = @url,
                Status       = @status,
                AppliedDate  = @appliedDate,
                FollowUpDate = @followUpDate,
                Notes        = @notes
            WHERE Id = @id;
            """;
        AddJobParameters(command, job);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = job.Id });

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> UpdateStatusAsync(int id, JobStatus status, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = "UPDATE dbo.JobApplications SET Status = @status WHERE Id = @id;";
        AddText(command, "@status", status.ToString(), 30);
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM dbo.JobApplications WHERE Id = @id;";
        command.Parameters.Add(new SqlParameter("@id", SqlDbType.Int) { Value = id });

        return await command.ExecuteNonQueryAsync(cancellationToken) > 0;
    }

    private async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var connection = new SqlConnection(_connectionString);
            try
            {
                await connection.OpenAsync(cancellationToken);
                return connection;
            }
            catch (SqlException ex) when (attempt < MaxAttempts && RetryableErrors.Contains(ex.Number))
            {
                await connection.DisposeAsync();
                await Task.Delay(RetryDelay, cancellationToken);
            }
            catch
            {
                await connection.DisposeAsync();
                throw;
            }
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
        AppliedDate = DateOnly.FromDateTime(reader.GetDateTime(6)),
        FollowUpDate = reader.IsDBNull(7) ? null : DateOnly.FromDateTime(reader.GetDateTime(7)),
        Notes = reader.IsDBNull(8) ? null : reader.GetString(8)
    };

    private static void AddJobParameters(SqlCommand command, JobApplication job)
    {
        AddText(command, "@company", job.Company.Trim(), 120);
        AddText(command, "@title", job.Title.Trim(), 120);
        AddText(command, "@location", ToDbText(job.Location), 120);
        AddText(command, "@url", ToDbText(job.Url), 500);
        AddText(command, "@status", job.Status.ToString(), 30);
        AddDate(command, "@appliedDate", job.AppliedDate);
        AddDate(command, "@followUpDate", job.FollowUpDate);
        AddText(command, "@notes", ToDbText(job.Notes), 2000);
    }

    // Parameters are given an explicit type and size so SQL Server never has to guess from the value
    // (and a NULL still knows which column type it is standing in for).
    private static void AddText(DbCommand command, string name, object? value, int size)
    {
        command.Parameters.Add(new SqlParameter(name, SqlDbType.NVarChar, size) { Value = value ?? DBNull.Value });
    }

    private static void AddDate(DbCommand command, string name, DateOnly? value)
    {
        command.Parameters.Add(new SqlParameter(name, SqlDbType.Date)
        {
            Value = value is { } date ? date.ToDateTime(TimeOnly.MinValue) : DBNull.Value
        });
    }

    private static object? ToDbText(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    // Without this, a search for "100%" or "a_b" would treat % and _ as wildcards.
    // T-SQL also treats [ as the start of a character range, so it is escaped too.
    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_").Replace("[", "\\[");
}
