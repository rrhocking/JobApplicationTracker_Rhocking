# syntax=docker/dockerfile:1

# ---- Stage 1: build ----------------------------------------------------------
# The SDK image has the compiler and is large; it is thrown away after publishing.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy only the project file first, so the (slow) restore step is cached
# and re-runs only when the package list changes, not on every code edit.
COPY src/JobTracker.Web/JobTracker.Web.csproj src/JobTracker.Web/
RUN dotnet restore src/JobTracker.Web/JobTracker.Web.csproj

COPY src/JobTracker.Web/ src/JobTracker.Web/
RUN dotnet publish src/JobTracker.Web/JobTracker.Web.csproj \
    --configuration Release --no-restore --output /app/publish

# ---- Stage 2: run ------------------------------------------------------------
# The ASP.NET runtime image is much smaller: it can run the app but not build it.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# SQLite file lives in /data, which is a volume so it survives container replacement.
# Created and owned here (as root) so the non-root "app" user can write to it.
RUN mkdir /data && chown app:app /data
VOLUME /data

COPY --from=build /app/publish .

# Settings are passed as environment variables (the __ becomes : in configuration).
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__JobTracker="Data Source=/data/jobtracker.db"

EXPOSE 8080

# Do not run as root inside the container.
USER app
ENTRYPOINT ["dotnet", "JobTracker.Web.dll"]
