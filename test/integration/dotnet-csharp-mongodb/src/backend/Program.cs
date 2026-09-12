using MichelOliveira.Com.ReactiveLock.Distributed.MongoDB;
using MongoDB.Driver;
using ReactiveLock.Integration.Shared;
using System.Net;

var builder = WebApplication.CreateSlimBuilder(args);
MongoDbAotMetadata.PreserveDocumentMembers();
builder.Services.AddSingleton<IMongoClient>(_ =>
    new MongoClient(builder.Configuration.GetConnectionString("mongodb")!));

builder.AddIntegrationApplication("mongodb", "MongoDB");
builder.Services.AddSingleton<MongoDbIntegrationStore>();
builder.Services.AddSingleton<MongoDbIntegrationBackend>();
builder.Services.AddSingleton<IWorkQueue>(services => services.GetRequiredService<MongoDbIntegrationBackend>());
builder.Services.AddSingleton<IPaymentStore>(services => services.GetRequiredService<MongoDbIntegrationBackend>());

builder.Services.InitializeDistributedMongoDbReactiveLock(
    Dns.GetHostName(),
    databaseName: "ReactiveLock",
    collectionName: "LockStatus");
var options = builder.Configuration.Get<DefaultOptions>()!;
builder.Services.AddDistributedMongoDbReactiveLock(
    Constant.DEFAULT_PROCESSOR_ERROR_THRESHOLD_NAME,
    busyThreshold: options.DEFAULT_PROCESSOR_CIRCUIT_ERROR_THRESHOLD_SECONDS);
builder.Services.AddDistributedMongoDbReactiveLock(Constant.REACTIVELOCK_HTTP_NAME);
builder.Services.AddDistributedMongoDbReactiveLock("mongodb");
builder.Services.AddDistributedMongoDbReactiveLock(
    Constant.REACTIVELOCK_API_PAYMENTS_SUMMARY_NAME,
    [async services => await services.GetRequiredService<PaymentSummaryService>().FlushWhileGateBlockedAsync()]);

var app = builder.Build();
await app.Services.GetRequiredService<MongoDbIntegrationStore>().InitializeAsync();
await app.UseDistributedMongoDbReactiveLockAsync();
app.MapIntegrationEndpoints();
app.Run();
