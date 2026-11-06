using 'main.bicep'

param appName = 'jobtracker'

// GitHub Container Registry image names must be lowercase.
// The publish workflow (next step) pushes this image.
param image = 'ghcr.io/rrhocking/jobapplicationtracker_rhocking:latest'

// The database password is never written in a file. Set it in your terminal first, for example in
// PowerShell:  $env:SQL_ADMIN_PASSWORD = "<your password>"   (the GitHub workflow uses a repository secret).
param sqlAdminPassword = readEnvironmentVariable('SQL_ADMIN_PASSWORD')
