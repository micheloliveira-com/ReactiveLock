FROM mcr.microsoft.com/dotnet/aspnet:10.0.2-alpine3.22 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.102-alpine3.22-aot AS build
RUN apk update \
    && apk add build-base zlib-dev
COPY ./nupkgs /src/nupkgs
RUN dotnet nuget add source /src/nupkgs --name local-temp

ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["backend/backend.csproj", "backend/"]
RUN dotnet restore "./backend/backend.csproj" -v d | tee restore.log \
    && grep -c "/src/nupkgs" restore.log | awk '{ if ($1 < 2) { print "Less than 2 local packages restored"; exit 1 } }'
COPY . .
WORKDIR "/src/backend"
RUN dotnet build "./backend.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./backend.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.2-alpine3.22 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["./backend"]
