# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# These ARGs allow for swapping out the base used to make the final image when debugging from VS
ARG LAUNCHING_FROM_VS
# This sets the base image for final, but only if LAUNCHING_FROM_VS has been defined
ARG FINAL_BASE_IMAGE=${LAUNCHING_FROM_VS:+aotdebug}

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:10.0.2-alpine3.22 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:10.0.102-alpine3.22-aot AS build
# Install clang/zlib1g-dev dependencies for publishing to native
RUN apk update \
    && apk add build-base zlib-dev grpc-plugins

ENV PROTOBUF_PROTOC=/usr/bin/protoc
ENV GRPC_PROTOC_PLUGIN=/usr/bin/grpc_csharp_plugin
ARG BUILD_CONFIGURATION=Release
ARG USE_REACTIVELOCK_PACKAGE_REFERENCES=false
RUN echo "USE_REACTIVELOCK_PACKAGE_REFERENCES=$USE_REACTIVELOCK_PACKAGE_REFERENCES"
WORKDIR /workspace
COPY . .

RUN if [ "$USE_REACTIVELOCK_PACKAGE_REFERENCES" = "true" ]; then \
      mkdir -p /src; \
      cp -R test/integration/dotnet-csharp-grpc/src/nupkgs /src/nupkgs; \
      find /src/nupkgs -maxdepth 1 -name '*.nupkg' -print -quit | grep -q . \
        || { echo "Package-reference mode requires .nupkg files in test/integration/dotnet-csharp-grpc/src/nupkgs"; exit 1; }; \
      dotnet nuget add source /src/nupkgs --name local-temp; \
    fi
RUN set -o pipefail; \
    dotnet restore "./test/integration/dotnet-csharp-grpc/src/backend/backend.csproj" \
      -p:UseReactiveLockPackageReferences=$USE_REACTIVELOCK_PACKAGE_REFERENCES -v d | tee restore.log; \
    if [ "$USE_REACTIVELOCK_PACKAGE_REFERENCES" = "true" ]; then \
      grep -c "/src/nupkgs" restore.log | awk '{ if ($1 < 2) { print "Less than 2 local package entries found during restore"; exit 1 } }'; \
    fi
WORKDIR "/workspace/test/integration/dotnet-csharp-grpc/src/backend"
RUN dotnet build "./backend.csproj" -p:UseReactiveLockPackageReferences=$USE_REACTIVELOCK_PACKAGE_REFERENCES -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./backend.csproj" -p:UseReactiveLockPackageReferences=$USE_REACTIVELOCK_PACKAGE_REFERENCES -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=true

# This stage is used as the base for the final stage when launching from VS to support debugging in regular mode (Default when not using the Debug configuration)
FROM base AS aotdebug
USER root
# Install GDB to support native debugging
RUN apk update \
    && apk add gdb
USER app

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM ${FINAL_BASE_IMAGE:-mcr.microsoft.com/dotnet/runtime-deps:10.0.2-alpine3.22} AS final
WORKDIR /app
EXPOSE 8080 8081
COPY --from=publish /app/publish .
ENTRYPOINT ["./backend"]
