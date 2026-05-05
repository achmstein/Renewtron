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

var sql = builder.AddSqlServer("sql", password: sqlPassword)
    .WithDataVolume("renewtron-sql-data")
    .WithLifetime(ContainerLifetime.Persistent)
    .PublishAsDockerComposeService((_, service) => service.Restart = "unless-stopped");

var renewtronDb = sql.AddDatabase("RenewtronDb");

var server = builder.AddProject<Projects.Renewtron_Server>("renewtron-server")
    .WithReference(renewtronDb)
    .WaitFor(sql)
    .WithEnvironment("AtoApi__Url", atoApiUrl)
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
