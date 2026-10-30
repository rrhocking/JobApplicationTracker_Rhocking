using 'main.bicep'

param appName = 'jobtracker'

// GitHub Container Registry image names must be lowercase.
// The publish workflow (next step) pushes this image.
param image = 'ghcr.io/rrhocking/jobapplicationtracker_rhocking:latest'
