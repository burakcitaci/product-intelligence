using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ProductHub.Api.Telemetry;

public static class OpenTelemetryLoggingExtensions
{
    public static ILoggingBuilder AddProductHubOpenTelemetry(
        this ILoggingBuilder logging,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var headers = configuration["OTEL_EXPORTER_OTLP_HEADERS"];
        if (string.IsNullOrWhiteSpace(headers))
            return logging;

        var protocol = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "http/protobuf";
        if (!protocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "ProductHub telemetry export requires OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf."
            );

        var logsEndpoint = GetEndpoint(configuration, "LOGS");
        if (logsEndpoint is not null)
        {
            ValidateEndpoint(logsEndpoint, configuration, environment);
            logging.AddOpenTelemetry(options =>
            {
                options.SetResourceBuilder(CreateResource(configuration, environment));
                options.IncludeFormattedMessage = true;
                options.IncludeScopes = true;
                options.ParseStateValues = true;
                options.AddOtlpExporter(exporter =>
                    ConfigureExporter(exporter, logsEndpoint, headers)
                );
            });
        }

        var tracesEndpoint = GetEndpoint(configuration, "TRACES");
        if (tracesEndpoint is not null)
        {
            ValidateEndpoint(tracesEndpoint, configuration, environment);
            logging.Services.AddOpenTelemetry().WithTracing(tracing =>
                tracing
                    .SetResourceBuilder(CreateResource(configuration, environment))
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddSqlClientInstrumentation()
                    .AddOtlpExporter(exporter =>
                        ConfigureExporter(exporter, tracesEndpoint, headers)
                    )
            );
        }

        return logging;
    }

    private static Uri? GetEndpoint(IConfiguration configuration, string signal)
    {
        var signalEndpoint = configuration[$"OTEL_EXPORTER_OTLP_{signal}_ENDPOINT"];
        if (!string.IsNullOrWhiteSpace(signalEndpoint))
            return new Uri(signalEndpoint, UriKind.Absolute);

        var endpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
        return string.IsNullOrWhiteSpace(endpoint)
            ? null
            : new Uri($"{endpoint.TrimEnd('/')}/v1/{signal.ToLowerInvariant()}", UriKind.Absolute);
    }

    private static ResourceBuilder CreateResource(
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? "product-hub-api";
        var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString();
        return ResourceBuilder
            .CreateDefault()
            .AddService(
                serviceName: serviceName,
                serviceVersion: serviceVersion,
                serviceInstanceId: Environment.MachineName
            )
            .AddAttributes([
                new KeyValuePair<string, object>(
                    "deployment.environment.name",
                    environment.EnvironmentName
                ),
            ]);
    }

    private static void ValidateEndpoint(
        Uri endpoint,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var allowInsecureLocalExporter =
            environment.IsDevelopment()
            && configuration.GetValue<bool>("OTEL_EXPORTER_OTLP_INSECURE");
        if (
            endpoint.Scheme != Uri.UriSchemeHttps
            && !endpoint.IsLoopback
            && !allowInsecureLocalExporter
        )
            throw new InvalidOperationException(
                "The OTLP endpoint must use HTTPS. Development containers may explicitly set "
                    + "OTEL_EXPORTER_OTLP_INSECURE=true for a trusted local exporter."
            );
    }

    private static void ConfigureExporter(
        OtlpExporterOptions exporter,
        Uri endpoint,
        string headers
    )
    {
        exporter.Endpoint = endpoint;
        exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
        exporter.Headers = headers;
        exporter.TimeoutMilliseconds = 10_000;
    }
}
