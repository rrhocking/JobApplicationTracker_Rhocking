using JobTracker.Web.Components;
using JobTracker.Web.Data;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);

// MVC for the page-based screens (list, add, edit, delete) and Blazor for the interactive board.
builder.Services.AddControllersWithViews();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

// Which database to use is a setting, not code: Database:Provider is "Sqlite" (the default, handy on
// your own computer) or "SqlServer" (Azure SQL in the cloud). In Azure it is set by the Bicep file.
var provider = builder.Configuration["Database:Provider"] ?? "Sqlite";

IJobRepository repository;
if (provider.Equals("SqlServer", StringComparison.OrdinalIgnoreCase))
{
    var sqlConnectionString = builder.Configuration.GetConnectionString("JobTracker")
        ?? throw new InvalidOperationException("Database:Provider is SqlServer but ConnectionStrings:JobTracker is not set.");

    var sqlRepository = new SqlServerJobRepository(sqlConnectionString);
    await sqlRepository.EnsureCreatedAsync();
    repository = sqlRepository;
}
else
{
    // SQLite is a single file. Set ConnectionStrings:JobTracker to put it somewhere else.
    var connectionString = builder.Configuration.GetConnectionString("JobTracker")
        ?? CreateDefaultConnectionString(builder.Environment.ContentRootPath);

    var sqliteRepository = new SqliteJobRepository(connectionString);
    sqliteRepository.EnsureCreated();
    repository = sqliteRepository;
}

// The repository opens a short-lived connection for every call and holds no state, so one
// shared instance is safe for MVC requests and for long-lived Blazor Server circuits alike.
//DEPENDENCY INJECTION
builder.Services.AddSingleton<IJobRepository>(repository);

var app = builder.Build();

// Optional sample data for screenshots: dotnet run --project src/JobTracker.Web --SeedDemoData=true
if (builder.Configuration.GetValue<bool>("SeedDemoData"))
{
    await DemoData.SeedIfEmptyAsync(repository);
}

if (!app.Environment.IsDevelopment())
{
    // Unhandled exceptions are re-run through /error, which shows a friendly page (no stack trace).
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

// Empty 404/403/etc. responses are re-run through /error/{code}, so a bad URL gets a friendly page.
app.UseStatusCodePagesWithReExecute("/error/{0}");

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Jobs}/{action=Index}/{id?}");
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

app.Run();

static string CreateDefaultConnectionString(string contentRoot)
{
    var dataDirectory = Path.Combine(contentRoot, "App_Data");
    Directory.CreateDirectory(dataDirectory);

    return new SqliteConnectionStringBuilder
    {
        DataSource = Path.Combine(dataDirectory, "jobtracker.db")
    }.ToString();
}
