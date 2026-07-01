# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/DailySummary.Functions/DailySummary.Functions.csproj -c Release -o /app/publish

# ---- runtime ----
FROM mcr.microsoft.com/azure-functions/dotnet-isolated:4-dotnet-isolated8.0 AS runtime

ENV AzureWebJobsScriptRoot=/home/site/wwwroot \
    AzureFunctionsJobHost__Logging__Console__IsEnabled=true \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright

# System deps: headed Firefox needs Xvfb; podcast transcription needs ffmpeg.
RUN apt-get update && apt-get install -y --no-install-recommends \
        xvfb ffmpeg ca-certificates \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish /home/site/wwwroot

# Install Firefox + OS deps for Playwright. (Requires the Playwright CLI from the published app.)
# TODO: verify the exact install invocation for your Playwright version, e.g.:
#   RUN dotnet /home/site/wwwroot/.playwright/... install firefox --with-deps
# or run the generated playwright script. Left explicit because it is version-specific.

# NOTE: headed Firefox must run under a virtual display. Wrap the host with xvfb-run,
# e.g. via a custom entrypoint:  xvfb-run -a <base-entrypoint>
