# syntax=docker/dockerfile:1.7
# Multi-arch build: compiles on the build machine's arch, publishes for the target (linux/arm64 for Oracle A1).
#   docker buildx build --platform linux/arm64 -f deploy/backend.Dockerfile -t comicplatform-api .

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
# global.json is intentionally not copied: the SDK image decides the feature band.
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/src/Domain/ComicPlatform.Domain.csproj backend/src/Domain/
COPY backend/src/Application/ComicPlatform.Application.csproj backend/src/Application/
COPY backend/src/Infrastructure/ComicPlatform.Infrastructure.csproj backend/src/Infrastructure/
COPY backend/src/Api/ComicPlatform.Api.csproj backend/src/Api/
RUN dotnet restore backend/src/Api/ComicPlatform.Api.csproj -a $TARGETARCH
COPY backend/src backend/src
RUN dotnet publish backend/src/Api/ComicPlatform.Api.csproj -c Release -a $TARGETARCH --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    COMIC_STORAGE_PATH=/data/comics \
    TEMP_STORAGE_PATH=/data/tmp \
    LOG_DIRECTORY=/data/logs \
    DOTNET_gcServer=0 \
    DOTNET_GCHeapAffinitizeMask=0
WORKDIR /app
RUN mkdir -p /data/comics /data/tmp /data/logs && chown -R $APP_UID /data
COPY --from=build /app .
USER $APP_UID
VOLUME ["/data"]
EXPOSE 8080 8443
ENTRYPOINT ["dotnet", "ComicPlatform.Api.dll"]
