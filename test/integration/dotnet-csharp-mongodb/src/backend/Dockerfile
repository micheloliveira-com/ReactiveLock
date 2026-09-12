FROM mcr.microsoft.com/dotnet/aspnet:10.0.2-alpine3.22 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.102-alpine3.22-aot AS build
RUN apk update \
    && apk add build-base zlib-dev
COPY ./dotnet-csharp-mongodb/src/nupkgs /src/nupkgs
RUN dotnet nuget add source /src/nupkgs --name local-temp

ARG BUILD_CONFIGURATION=Release
WORKDIR /workspace/test/integration
COPY ["shared/ReactiveLock.Integration.Shared/ReactiveLock.Integration.Shared.csproj", "shared/ReactiveLock.Integration.Shared/"]
COPY ["dotnet-csharp-mongodb/src/backend/backend.csproj", "dotnet-csharp-mongodb/src/backend/"]
RUN dotnet restore "./dotnet-csharp-mongodb/src/backend/backend.csproj" -p:UseReactiveLockPackageReferences=true -v d | tee restore.log \
    && grep -c "/src/nupkgs" restore.log | awk '{ if ($1 < 2) { print "Less than 2 local packages restored"; exit 1 } }'
COPY . .
WORKDIR "/workspace/test/integration/dotnet-csharp-mongodb/src/backend"
RUN dotnet build "./backend.csproj" -p:UseReactiveLockPackageReferences=true -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./backend.csproj" -p:UseReactiveLockPackageReferences=true -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.2-alpine3.22 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["./backend"]
