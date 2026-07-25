# syntax=docker/dockerfile:1.7

# ---- Frontend ----
# The SPA is built here rather than copied from web/dist: dist/ is gitignored,
# so a CI checkout never carries it and an image built from one would ship an
# empty wwwroot and serve no UI at all.
FROM node:22-alpine AS frontend
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN npm run build && mkdir -p /frontend-dist && cp -r dist/. /frontend-dist/

# ---- Build the .NET app ----
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props EventFinder.slnx ./
COPY src/EventFinder.Core/EventFinder.Core.csproj src/EventFinder.Core/
COPY src/EventFinder.Data/EventFinder.Data.csproj src/EventFinder.Data/
COPY src/EventFinder.Ingestion/EventFinder.Ingestion.csproj src/EventFinder.Ingestion/
COPY src/EventFinder.Api/EventFinder.Api.csproj src/EventFinder.Api/
RUN dotnet restore src/EventFinder.Api/EventFinder.Api.csproj
COPY src/ src/
COPY data/ data/
COPY sources.yaml ./
RUN dotnet publish src/EventFinder.Api/EventFinder.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# ---- Runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV DOTNET_RUNNING_IN_CONTAINER=true \
    ASPNETCORE_URLS=http://+:8080 \
    EVENTFINDER__Database__Path=/data/eventfinder.db \
    EVENTFINDER__Data__Directory=/data

RUN mkdir -p /data && chmod 700 /data
COPY --from=build /app/publish ./
COPY --from=frontend /frontend-dist ./wwwroot

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1
ENTRYPOINT ["dotnet", "EventFinder.Api.dll"]
