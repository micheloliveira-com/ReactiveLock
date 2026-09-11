using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Text.Json;

namespace ReactiveLock.Integration.Shared;

public static class IntegrationApplicationExtensions
{
    public static WebApplicationBuilder AddIntegrationApplication(
        this WebApplicationBuilder builder,
        string channelLockName,
        string displayName)
    {
        builder.Services.AddSingleton(new IntegrationBackendOptions(channelLockName, displayName));
        builder.Services.AddOptions<DefaultOptions>().Bind(builder.Configuration);
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, IntegrationJsonContext.Default);
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        builder.Services.AddHttpClient(Constant.DEFAULT_PROCESSOR_NAME, client =>
            client.BaseAddress = new Uri(builder.Configuration.GetConnectionString(Constant.DEFAULT_PROCESSOR_NAME)!))
            .AddHttpMessageHandler<CountingHandler>();
        builder.Services.AddHttpClient(Constant.FALLBACK_PROCESSOR_NAME, client =>
            client.BaseAddress = new Uri(builder.Configuration.GetConnectionString(Constant.FALLBACK_PROCESSOR_NAME)!))
            .AddHttpMessageHandler<CountingHandler>();

        builder.Services.AddTransient<CountingHandler>();
        builder.Services.AddSingleton<PaymentService>();
        builder.Services.AddSingleton<RunningPaymentsSummaryData>();
        builder.Services.AddSingleton<ConsoleWriterService>();
        builder.Services.AddSingleton<PaymentSummaryService>();
        builder.Services.AddSingleton<PaymentBatchInserterService>();
        builder.Services.AddSingleton<PaymentProcessorService>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, QueueWorker>());

        if (builder.Environment.IsProduction() || builder.Environment.IsDevelopment())
        {
            builder.Logging.ClearProviders();
            builder.Logging.SetMinimumLevel(LogLevel.Error);
        }

        return builder;
    }

    public static WebApplication MapIntegrationEndpoints(this WebApplication app)
    {
        var api = app.MapGroup("/");
        api.MapGet("/", () => Results.Ok());
        api.MapPost("payments", (HttpContext context, [FromServices] PaymentService service) =>
            service.EnqueuePaymentAsync(context));
        api.MapGet("/payments-summary", (
            [FromQuery] DateTimeOffset? from,
            [FromQuery] DateTimeOffset? to,
            [FromServices] PaymentSummaryService service) =>
            service.GetPaymentsSummaryAsync(from, to));
        api.MapPost("/purge-payments", (
            HttpContext context,
            [FromServices] PaymentService service) =>
            service.PurgePaymentsAsync(context.RequestAborted));
        return app;
    }
}
