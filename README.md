# ReactiveLock

ReactiveLock is a .NET 9 library for reactive, distributed lock coordination. It allows multiple application instances to track busy/idle state and react to state changes using async handlers.

It supports both in-process and distributed synchronization. Redis is the default distributed backend.

## Packages

| Package                             | Description                                               |
|-------------------------------------|-----------------------------------------------------------|
| ReactiveLock.Core                  | Core abstractions and in-process lock coordination        |
| ReactiveLock.DependencyInjection   | Adds DI and named resolution for distributed backends     |
| ReactiveLock.Distributed.Redis     | Redis-based distributed lock synchronization              |

> Use only ReactiveLock.Core if you don't need distributed coordination.

## Installation

In-process only:

```bash
dotnet add package ReactiveLock.Core
```

Distributed with Redis:

```bash
dotnet add package ReactiveLock.Core
dotnet add package ReactiveLock.DependencyInjection
dotnet add package ReactiveLock.Distributed.Redis
```

## Usage

### Local-only (in-process)

```csharp
using MichelOliveira.Com.ReactiveLock.Core;

var state = new ReactiveLockTrackerState();
await state.SetLocalStateBlockedAsync();

var tasks = Enumerable.Range(1, 3).Select(i =>
    Task.Run(async () => {
        Console.WriteLine($"[Task {i}] Waiting...");
        await state.WaitIfBlockedAsync();
        Console.WriteLine($"[Task {i}] Proceeded.");
    })
).ToArray();

await Task.Delay(1000);
await state.SetLocalStateUnblockedAsync();
await Task.WhenAll(tasks);

Console.WriteLine("Done.");
```

### Increment / Decrement

```csharp
var state = new ReactiveLockTrackerState();
var controller = new ReactiveLockTrackerController(state);

await controller.IncrementAsync();
await Task.Delay(300);
await controller.DecrementAsync();
```

## Distributed HTTP Client Request Counter (Redis)

### Setup

```csharp
builder.Services.InitializeDistributedRedisReactiveLock(Dns.GetHostName());
builder.Services.AddDistributedRedisReactiveLock("http");
builder.Services.AddTransient<CountingHandler>();

builder.Services.AddHttpClient("http", client =>
    client.BaseAddress = new Uri(builder.Configuration.GetConnectionString("http")!))
    .AddHttpMessageHandler<CountingHandler>();

var app = builder.Build();
await app.UseDistributedRedisReactiveLockAsync();
```

### CountingHandler

```csharp
public class CountingHandler : DelegatingHandler
{
    private readonly IReactiveLockTrackerController _controller;

    public CountingHandler(IReactiveLockTrackerFactory factory)
    {
        _controller = factory.GetTrackerController("http");
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _controller.IncrementAsync();
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            await _controller.DecrementAsync();
        }
    }
}
```

### Expected Behavior

- Each HTTP request increments the "http" lock counter.
- On response, the counter is decremented.
- Lock state is shared across all application instances.
- You can use the lock state to:
  - Check if any requests are active.
  - Wait for all requests to complete.

### Use Case Example

```csharp
var state = factory.GetTrackerState("http");

if (await state.IsBlockedAsync())
{
    Console.WriteLine("HTTP requests active.");
}

await state.WaitIfBlockedAsync();
Console.WriteLine("No active HTTP requests.");
```

## Requirements

- .NET 9 SDK

## License

MIT License
