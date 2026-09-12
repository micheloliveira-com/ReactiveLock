# ReactiveLock gRPC integration fixture

This fixture runs two backend instances using peer-to-peer gRPC for ReactiveLock coordination and payment replication. Redis provides the shared work queue, while each backend keeps its replicated payment state in memory.

Local builds use the ReactiveLock source projects by default. To use packed NuGet packages instead:

```bash
dotnet build src/backend/backend.csproj -p:UseReactiveLockPackageReferences=true
```

To run the complete fixture after starting the shared payment-processor network:

```bash
cd src
docker compose up --build
```
