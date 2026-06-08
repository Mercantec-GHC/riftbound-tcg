var builder = DistributedApplication.CreateBuilder(args);

var cache = builder.AddRedis("cache");

var tcgapi = builder.AddExternalService("tcgapi", "https://api.tcgplayer.com"); // TCG Api 

var server = builder.AddProject<Projects.riftbound_tcg_Server>("server")
    .WithReference(cache)
    .WaitFor(cache)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
