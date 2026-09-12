FROM mcr.microsoft.com/dotnet/aspnet:10.0.2-alpine3.22 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0.102-alpine3.22-aot AS build
RUN apk update \
    && apk add build-base zlib-dev
ARG BUILD_CONFIGURATION=Release
ARG USE_REACTIVELOCK_PACKAGE_REFERENCES=false
RUN echo "USE_REACTIVELOCK_PACKAGE_REFERENCES=$USE_REACTIVELOCK_PACKAGE_REFERENCES"
WORKDIR /workspace
COPY . .
RUN if [ "$USE_REACTIVELOCK_PACKAGE_REFERENCES" = "true" ]; then \
      mkdir -p /src; \
      cp -R test/integration/dotnet-csharp-mongodb/src/nupkgs /src/nupkgs; \
      find /src/nupkgs -maxdepth 1 -name '*.nupkg' -print -quit | grep -q . \
        || { echo "Package-reference mode requires .nupkg files in test/integration/dotnet-csharp-mongodb/src/nupkgs"; exit 1; }; \
      dotnet nuget add source /src/nupkgs --name local-temp; \
    fi
RUN set -o pipefail; \
    dotnet restore "./test/integration/dotnet-csharp-mongodb/src/backend/backend.csproj" \
      -p:UseReactiveLockPackageReferences=$USE_REACTIVELOCK_PACKAGE_REFERENCES -v d | tee restore.log; \
    if [ "$USE_REACTIVELOCK_PACKAGE_REFERENCES" = "true" ]; then \
      grep -c "/src/nupkgs" restore.log | awk '{ if ($1 < 2) { print "Less than 2 local package entries found during restore"; exit 1 } }'; \
    fi
WORKDIR "/workspace/test/integration/dotnet-csharp-mongodb/src/backend"
RUN dotnet build "./backend.csproj" -p:UseReactiveLockPackageReferences=$USE_REACTIVELOCK_PACKAGE_REFERENCES -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./backend.csproj" -p:UseReactiveLockPackageReferences=$USE_REACTIVELOCK_PACKAGE_REFERENCES -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true

FROM mcr.microsoft.com/dotnet/runtime-deps:10.0.2-alpine3.22 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=publish /app/publish .
ENTRYPOINT ["./backend"]
