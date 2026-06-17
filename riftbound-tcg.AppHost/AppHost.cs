var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume();

var database = postgres.AddDatabase("riftbound");

var server = builder.AddProject<Projects.riftbound_tcg_Server>("server")
    .WithReference(database)
    .WaitFor(database)
    .WithHttpHealthCheck("/health")
    .WithExternalHttpEndpoints();

var webfrontend = builder.AddViteApp("webfrontend", "../frontend")
    .WithReference(server)
    .WaitFor(server);


var devtunnel = builder.AddDevTunnel("devtunnel")
    .WithReference(webfrontend)
    .WithAnonymousAccess();

server.PublishWithContainerFiles(webfrontend, "wwwroot");

builder.Build().Run();
