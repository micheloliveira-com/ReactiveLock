# RabbitMQ integration test

This environment follows the existing k6 integration-test pattern while using
`ReactiveLock.Distributed.RabbitMQ` for distributed lock propagation.

RabbitMQ carries ReactiveLock status messages, the sample application's work queue,
and replicated payment events. RabbitMQ is the fixture's only data transport.

Run from `src` after placing locally packed NuGet packages in `src/nupkgs`:

```sh
docker compose up -d --build
```

For a direct local build against the repository projects instead of packed packages:

```sh
dotnet build backend/backend.csproj -p:UseLocalReactiveLockProjects=true
```
