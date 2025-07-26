# ReactiveLock

ReactiveLock is a .NET 9 library for reactive, distributed lock coordination. It lets multiple application instances track “busy”/“idle” state and react to changes via async handlers. Redis is provided out-of-the-box, but you can plug in any backend.

---

## Packages

- **ReactiveLock.Core**\
  Core abstractions and in‑process state management. You can use it if no distributed strategy is needed.

- **ReactiveLock.Distributed.Redis**\
  Redis‑backed distributed implementation of `ReactiveLock.Core`.

- **ReactiveLock.DependencyInjection**\
  Dependency Injection extensions for controller, state, and factory services with keyed resolution support for `ReactiveLock.Distributed.*` libs..

---

## Concepts

- **Tracker Controller** (`IReactiveLockTrackerController`)\
  Call `IncrementAsync()` / `DecrementAsync()` to mark this instance busy or idle.

- **Tracker State** (`IReactiveLockTrackerState`)\
  Call `IsBlockedAsync()` or `WaitIfBlockedAsync()` to observe or wait for global idle state.

- **Store** (`IReactiveLockTrackerStore`)\
  Persists and propagates instance states. Redis store is included; custom stores are supported.

---

## Installation


If you want **local, in-process locking only**:
```bash
dotnet add package ReactiveLock.Core
```

If you want distributed locking (Redis-based), install the distributed packages in addition to Core:
```bash
dotnet add package ReactiveLock.Core
dotnet add package ReactiveLock.DependencyInjection
dotnet add package ReactiveLock.Distributed.Redis
```

---

## Usage

### Simplest: Local-Only
```csharp
using MichelOliveira.Com.ReactiveLock.Core;

var state = new ReactiveLockTrackerState();

Console.WriteLine("Setting local state to BLOCKED...");
await state.SetLocalStateBlockedAsync();

// Simulate multiple tasks waiting on the lock
var waitingTasks = new[]
{
    Task.Run(async () => {
        Console.WriteLine("[Task 1] Waiting for lock to unblock...");
        await state.WaitIfBlockedAsync();
        Console.WriteLine("[Task 1] Proceeded after unblock.");
    }),

    Task.Run(async () => {
        Console.WriteLine("[Task 2] Waiting for lock to unblock...");
        await state.WaitIfBlockedAsync();
        Console.WriteLine("[Task 2] Proceeded after unblock.");
    }),

    Task.Run(async () => {
        Console.WriteLine("[Task 3] Waiting for lock to unblock...");
        await state.WaitIfBlockedAsync();
        Console.WriteLine("[Task 3] Proceeded after unblock.");
    })
};

// Simulate control logic in a separate task
var controlTask = Task.Run(async () =>
{
    await Task.Delay(1500); // Wait before unblocking
    Console.WriteLine("[Controller] Unblocking lock...");
    await state.SetLocalStateUnblockedAsync();
});

// Wait for all tasks to complete
await Task.WhenAll(waitingTasks.Concat([controlTask]));

Console.WriteLine("✅ All tasks finished. Hello, World!");

```
You'll see: 
```
Setting local state to BLOCKED...
[Task 1] Waiting for lock to unblock...
[Task 2] Waiting for lock to unblock...
[Task 3] Waiting for lock to unblock...
[Controller] Unblocking lock...
[Task 1] Proceeded after unblock.
[Task 2] Proceeded after unblock.
[Task 3] Proceeded after unblock.
✅ All tasks finished. Hello, World!
```

### More Complete Example: Using Controller Increment/Decrement with Blocking:


```csharp
using MichelOliveira.Com.ReactiveLock.Core;

var state = new ReactiveLockTrackerState();
var controller = new ReactiveLockTrackerController(state);

Console.WriteLine("Setting local state to BLOCKED...");
await state.SetLocalStateBlockedAsync();

var waitingTasks = new[]
{
    Task.Run(async () => {
        Console.WriteLine("[Task 1] Waiting for lock to unblock...");
        await state.WaitIfBlockedAsync();
        Console.WriteLine("[Task 1] Proceeded after unblock.");

        Console.WriteLine("[Task 1] Incrementing lock...");
        await controller.IncrementAsync();

        await Task.Delay(500); // simulate some work

        Console.WriteLine("[Task 1] Decrementing lock...");
        await controller.DecrementAsync();
    }),

    Task.Run(async () => {
        Console.WriteLine("[Task 2] Waiting for lock to unblock...");
        await state.WaitIfBlockedAsync();
        Console.WriteLine("[Task 2] Proceeded after unblock.");

        Console.WriteLine("[Task 2] Incrementing lock...");
        await controller.IncrementAsync();

        await Task.Delay(300); // simulate some work

        Console.WriteLine("[Task 2] Decrementing lock...");
        await controller.DecrementAsync();
    }),

    Task.Run(async () => {
        Console.WriteLine("[Task 3] Waiting for lock to unblock...");
        await state.WaitIfBlockedAsync();
        Console.WriteLine("[Task 3] Proceeded after unblock.");

        Console.WriteLine("[Task 3] Incrementing lock...");
        await controller.IncrementAsync();

        await Task.Delay(700); // simulate some work

        Console.WriteLine("[Task 3] Decrementing lock...");
        await controller.DecrementAsync();
    }),
};

// Control task to unblock after delay
var controlTask = Task.Run(async () =>
{
    await Task.Delay(1500); // Wait before unblocking
    Console.WriteLine("[Controller] Unblocking lock...");
    await state.SetLocalStateUnblockedAsync();
});

await Task.WhenAll(waitingTasks.Concat(new[] { controlTask }));

Console.WriteLine("✅ All tasks finished. Hello, World!");

```
You'll see:
```
Setting local state to BLOCKED...
[Task 1] Waiting for lock to unblock...
[Task 2] Waiting for lock to unblock...
[Task 3] Waiting for lock to unblock...
[Controller] Unblocking lock...
[Task 3] Proceeded after unblock.
[Task 3] Incrementing lock...
[Task 1] Proceeded after unblock.
[Task 1] Incrementing lock...
[Task 2] Proceeded after unblock.
[Task 2] Incrementing lock...
[Task 2] Decrementing lock...
[Task 1] Decrementing lock...
[Task 3] Decrementing lock...
✅ All tasks finished. Hello, World!
```

### HTTP Client Request Counter Sample (Distributed with Dependency Injection):
Register services and HTTP client in `Program.cs`:
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
Implement CountingHandler for `HttpClient`:
```csharp
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MichelOliveira.Com.ReactiveLock.Core;
using MichelOliveira.Com.ReactiveLock.DependencyInjection;

public class CountingHandler : DelegatingHandler
{
    private readonly IReactiveLockTrackerController _reactiveLockTrackerController;

    public CountingHandler(IReactiveLockTrackerFactory reactiveLockTrackerFactory)
    {
        _reactiveLockTrackerController = reactiveLockTrackerFactory.GetTrackerController("http");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await _reactiveLockTrackerController.IncrementAsync().ConfigureAwait(false);

        try
        {
            return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await _reactiveLockTrackerController.DecrementAsync().ConfigureAwait(false);
        }
    }
}
```

### **Expected Behavior**:

- Each outgoing HTTP request **increments** the `"http"` lock counter by calling `IncrementAsync()`.
- When the request completes (successfully or with failure), the counter is **decremented** via `DecrementAsync()`.
- This allows you to **track how many HTTP requests are active concurrently** in your application.
- When using the **Redis-backed distributed store**, this lock state is **synchronized across all instances** of your application:
  - The state of active requests reflects **all instances globally**, not just locally.
  - Any instance observing the lock via `IReactiveLockTrackerState` will see the **aggregated busy/idle state** from all distributed nodes.
- You can use `IReactiveLockTrackerState` for `"http"` to:
  - Check if there are **any active requests across the cluster** (`IsBlockedAsync()` returns `true` if the global count > 0).
  - **Wait** until all requests across all instances complete using `WaitIfBlockedAsync()`.
- This distributed coordination is useful for:
  - Monitoring overall concurrency in a microservice or distributed system.
  - Implementing **graceful shutdowns** or maintenance windows, waiting for all active HTTP requests in the cluster to finish.
  - Triggering logic when the system is globally idle or busy.

---

### Example usage to check active requests or wait:

```csharp
var state = factory.GetTrackerState("http");

// Check if any HTTP requests are currently active:
bool busy = await state.IsBlockedAsync();

if (busy)
{
    Console.WriteLine("HTTP client is currently busy with active requests.");
}

// Wait until all HTTP requests finish:
await state.WaitIfBlockedAsync();

Console.WriteLine("All HTTP requests completed. Safe to shutdown or continue.");
```
---

## Requirements

- .NET 9

---

## License

MIT © Michel Oliveira

