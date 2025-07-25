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

```bash
dotnet add package ReactiveLock.Core
dotnet add package ReactiveLock.DependencyInjection
dotnet add package ReactiveLock.Distributed.Redis
```

---

## Registration

In `Program.cs` (or equivalent):

```csharp
// 1. Register factory and Redis connection
builder.Services.InitializeDistributedRedisReactiveLock("instance-id");

builder.Services.AddSingleton<IConnectionMultiplexer>(
    ConnectionMultiplexer.Connect("localhost:6379"));

// 2. Register one or more locks
builder.Services.AddDistributedRedisReactiveLock(
    lockKey: "my-lock",
    onLockedHandlers:   [ sp => Task.CompletedTask ],
    onUnlockedHandlers: [ sp => Task.CompletedTask ]);

// 3. After Build(), start subscription loop
var app = builder.Build();
await app.UseDistributedRedisReactiveLockAsync();
```

---

## Usage

```csharp
var factory    = app.Services.GetRequiredService<IReactiveLockTrackerFactory>();
var controller = factory.GetTrackerController("my-lock");
var state      = factory.GetTrackerState("my-lock");

// Mark busy
await controller.IncrementAsync();
// … work …
await controller.DecrementAsync();

// React to global state
if (await state.IsBlockedAsync())
    /* handle blocked */;

await state.WaitIfBlockedAsync();
```

---

## Requirements

- .NET 9

---

## License

MIT © Michel Oliveira

