using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;

namespace ProductHub.Api.Telemetry;

public static class OpenTelemetryLoggingExtensions
{
    public static ILoggingBuilder AddProductHubOpenTelemetry(
        this ILoggingBuilder logging,
        IConfiguration configuration,
        IHostEnvironment environment
    )
    {
        var endpoint = configuration["OTEL_EXPORTER_OTLP_LOGS_ENDPOINT"];
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            var baseEndpoint = configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
            if (!string.IsNullOrWhiteSpace(baseEndpoint))
                endpoint = $"{baseEndpoint.TrimEnd('/')}/v1/logs";
        }

        var headers = configuration["OTEL_EXPORTER_OTLP_HEADERS"];
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(headers))
            return logging;

        var protocol = configuration["OTEL_EXPORTER_OTLP_PROTOCOL"] ?? "http/protobuf";
        if (!protocol.Equals("http/protobuf", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "ProductHub log export requires OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf."
            );

        var endpointUri = new Uri(endpoint, UriKind.Absolute);
        var allowInsecureLocalExporter =
            environment.IsDevelopment()
            && configuration.GetValue<bool>("OTEL_EXPORTER_OTLP_INSECURE");
        if (
            endpointUri.Scheme != Uri.UriSchemeHttps
            && !endpointUri.IsLoopback
            && !allowInsecureLocalExporter
        )
            throw new InvalidOperationException(
                "The OTLP endpoint must use HTTPS. Development containers may explicitly set "
                    + "OTEL_EXPORTER_OTLP_INSECURE=true for a trusted local exporter."
            );

        var serviceName = configuration["OTEL_SERVICE_NAME"] ?? "product-hub-api";
        var serviceVersion = typeof(Program).Assembly.GetName().Version?.ToString();
        var resource = ResourceBuilder
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

        logging.AddOpenTelemetry(options =>
        {
            options.SetResourceBuilder(resource);
            options.IncludeFormattedMessage = true;
            options.IncludeScopes = true;
            options.ParseStateValues = true;
            options.AddOtlpExporter(exporter =>
            {
                exporter.Endpoint = endpointUri;
                exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
                exporter.Headers = headers;
                exporter.TimeoutMilliseconds = 10_000;
            });
        });

        return logging;
    }
}
