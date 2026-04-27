var builder = DistributedApplication.CreateBuilder(args);

builder.AddDockerComposeEnvironment("renewtron")
    .WithDashboard(dashboard => dashboard.WithHostPort(18889));

var sqlPassword = builder.AddParameter("sql-password", secret: true);

var sql = builder.AddSqlServer("sql", password: sqlPassword)
    .WithDataVolume("renewtron-sql-data")
    .WithLifetime(ContainerLifetime.Persistent)
    .PublishAsDockerComposeService((_, service) => service.Restart = "unless-stopped");

var renewtronDb = sql.AddDatabase("RenewtronDb");

var server = builder.AddProject<Projects.Renewtron_Server>("renewtron-server")
    .WithReference(renewtronDb)
    .WaitFor(sql)
    .WithHttpHealthCheck("/health")
    .PublishAsDockerFile()
    .PublishAsDockerComposeService((_, service) =>
    {
        service.Restart = "unless-stopped";
        service.Ports.Clear(); // no host port — Caddy proxies internally
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
    // Publish: nginx-served static React, fronted by Caddy
    builder.AddDockerfile("renewtron-web", "../frontend")
        .WithBuildArg("VITE_API_URL", "https://businessnames.applyforanabn.au")
        .PublishAsDockerComposeService((_, service) => service.Restart = "unless-stopped");

    builder.AddDockerfile("caddy", "../caddy")
        .WithVolume("caddy-data", "/data")
        .WithVolume("caddy-config", "/config")
        .WaitFor(server)
        .PublishAsDockerComposeService((_, service) =>
        {
            service.Restart = "unless-stopped";
            service.Ports.Clear();
            service.Ports.Add("80:80");
            service.Ports.Add("443:443");
            service.Ports.Add("443:443/udp");
        });
}

builder.Build().Run();
