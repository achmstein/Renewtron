using Aspire.Hosting.Docker.Resources.ComposeNodes;
using Aspire.Hosting.Docker.Resources.ServiceNodes;

var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("renewtron-compose")
    .WithDashboard(dashboard => dashboard.WithHostPort(18886))
    .ConfigureComposeFile(compose =>
    {
        compose.AddNetwork(new Network
        {
            Name = "caddy",
            External = true
        });
    });

var sqlPassword = builder.AddParameter("sql-password", secret: true);
var atoApiUrl = builder.AddParameter("ato-api-url");

var securityApiKey = builder.AddParameter("security-api-key", secret: true, value: ""); // empty = auth not enforced
var stripeSecretKey = builder.AddParameter("stripe-secret-key", secret: true, value: "");
var sendGridApiKey = builder.AddParameter("sendgrid-api-key", secret: true, value: "");
var ontraportApiAppId = builder.AddParameter("ontraport-api-app-id", secret: true, value: "");
var ontraportApiKey = builder.AddParameter("ontraport-api-key", secret: true, value: "");
var encryptionKey = builder.AddParameter("encryption-key", secret: true, value: "");
var encryptionIv = builder.AddParameter("encryption-iv", secret: true, value: "");
var asicEmail = builder.AddParameter("asic-email", secret: true, value: "");
var asicCardNumber = builder.AddParameter("asic-card-number", secret: true, value: "");
var asicCardholderName = builder.AddParameter("asic-cardholder-name", secret: true, value: "");
var asicExpiryMonth = builder.AddParameter("asic-expiry-month", secret: true, value: "");
var asicExpiryYear = builder.AddParameter("asic-expiry-year", secret: true, value: "");
var asicCvc = builder.AddParameter("asic-cvc", secret: true, value: "");

var sql = builder.AddSqlServer("sql", password: sqlPassword)
    .WithDataVolume("renewtron-sql-data")
    .WithLifetime(ContainerLifetime.Persistent)
    .PublishAsDockerComposeService((_, service) => service.Restart = "unless-stopped");

var renewtronDb = sql.AddDatabase("RenewtronDb");

var server = builder.AddProject<Projects.Renewtron_Server>("renewtron-server")
    .WithReference(renewtronDb)
    .WaitFor(sql)
    .WithEnvironment("AtoApi__Url", atoApiUrl)
    .WithEnvironment("Security__ApiKey", securityApiKey)
    .WithEnvironment("Stripe__SecretKey", stripeSecretKey)
    .WithEnvironment("SendGrid__ApiKey", sendGridApiKey)
    .WithEnvironment("Ontraport__ApiAppId", ontraportApiAppId)
    .WithEnvironment("Ontraport__ApiKey", ontraportApiKey)
    .WithEnvironment("Encryption__Key", encryptionKey)
    .WithEnvironment("Encryption__IV", encryptionIv)
    .WithEnvironment("Asic__Email", asicEmail)
    .WithEnvironment("Asic__CardNumber", asicCardNumber)
    .WithEnvironment("Asic__CardholderName", asicCardholderName)
    .WithEnvironment("Asic__ExpiryMonth", asicExpiryMonth)
    .WithEnvironment("Asic__ExpiryYear", asicExpiryYear)
    .WithEnvironment("Asic__Cvc", asicCvc)
    // Writable settings overrides land here. The volume is bind-mounted so admin
    // edits to AtoAgent etc. survive container/image churn.
    .WithEnvironment("Storage__OverridesPath", "/data/settings.overrides.json")
    .WithHttpHealthCheck("/health")
    .PublishAsDockerFile()
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Restart = "unless-stopped";
        service.Ports.Clear(); // no host port — nginx in renewtron-web proxies internally
        service.Volumes.Add(new Volume
        {
            Name = "renewtron-data",
            Type = "bind",
            Source = "./data",
            Target = "/data"
        });
    });

if (builder.ExecutionContext.IsRunMode)
{
    // Local dev: Vite dev server with HMR + proxy to /api
    var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
        .WithReference(server)
        .WaitFor(server);

    server.PublishWithContainerFiles(webfrontend, "wwwroot");
}
else
{
    // Publish: nginx-served static React, fronted by the host's caddy-docker-proxy.
    // nginx fans /api, /hangfire, /health back to renewtron-server on the project's
    // internal network. Only this service joins `caddy` (so the global proxy reaches it).
    builder.AddDockerfile("renewtron-web", "../frontend")
        .WithBuildArg("VITE_API_URL", "https://businessnames.applyforanabn.au")
        .WaitFor(server)
        .PublishAsDockerComposeService((_, service) =>
        {
            service.Restart = "unless-stopped";
            service.Ports.Clear();
            service.Networks.Add("caddy");
            service.Labels["caddy"] = "businessnames.applyforanabn.au";
            service.Labels["caddy.reverse_proxy"] = "{{upstreams 80}}";
        });
}

builder.Build().Run();
