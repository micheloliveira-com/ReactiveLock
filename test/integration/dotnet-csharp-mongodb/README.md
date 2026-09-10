# ReactiveLock MongoDB integration fixture

This fixture runs two backend instances using MongoDB for both ReactiveLock coordination and the sample application's queue/payment data.

MongoDB runs as a single-node replica set because the provider uses native change streams for reactive lock propagation. The provider stores one renewable lease document per lock and application instance, backed by lookup and TTL indexes.

To use source projects instead of packed NuGet packages:

```bash
dotnet build src/backend/backend.csproj -p:UseLocalReactiveLockProjects=true
```

To run the complete fixture after starting the shared payment-processor network:

```bash
cd src
docker compose up --build
```
