# ReactiveLock Redis integration fixture

This fixture runs two backend instances using Redis for ReactiveLock coordination, the shared work queue, and payment storage.

Local builds use the ReactiveLock source projects by default. To use packed NuGet packages instead:

```bash
dotnet build src/backend/backend.csproj -p:UseReactiveLockPackageReferences=true
```

To run the complete fixture after starting the shared payment-processor network:

```bash
cd src
docker compose up --build
```

The Docker build uses source project references by default. To test the packed packages in `src/nupkgs` instead:

```bash
USE_REACTIVELOCK_PACKAGE_REFERENCES=true docker compose up --build
```
