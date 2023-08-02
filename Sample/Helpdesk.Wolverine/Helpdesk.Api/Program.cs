using System.Text.Json.Serialization;
using Helpdesk.Api.Core.Kafka;
using Helpdesk.Api.Core.SignalR;
using Helpdesk.Api.Incidents;
using Helpdesk.Api.Incidents.GetCustomerIncidentsSummary;
using Helpdesk.Api.Incidents.GetIncidentDetails;
using Helpdesk.Api.Incidents.GetIncidentHistory;
using Helpdesk.Api.Incidents.GetIncidentShortInfo;
using JasperFx.CodeGeneration;
using Marten;
using Marten.Events.Daemon.Resiliency;
using Marten.Events.Projections;
using Marten.Services.Json;
using Microsoft.AspNetCore.SignalR;
using Oakton;
using Oakton.Resources;
using Weasel.Core;
using Wolverine;
using Wolverine.Http;
using Wolverine.Marten;
using JsonOptions = Microsoft.AspNetCore.Http.Json.JsonOptions;

var builder = WebApplication.CreateBuilder(args);


builder.Services
    .AddEndpointsApiExplorer()
    .AddSwaggerGen()
    .AddMarten(sp =>
    {
        var options = new StoreOptions();

        var schemaName = Environment.GetEnvironmentVariable("SchemaName") ?? "Helpdesk";
        options.Events.DatabaseSchemaName = schemaName;
        options.DatabaseSchemaName = schemaName;
        options.Connection(builder.Configuration.GetConnectionString("Incidents") ??
                           throw new InvalidOperationException());

        options.UseDefaultSerialization(
            EnumStorage.AsString,
            nonPublicMembersStorage: NonPublicMembersStorage.All,
            serializerType: SerializerType.SystemTextJson
        );

        options.Projections.LiveStreamAggregation<Incident>();
        options.Projections.Add<IncidentHistoryTransformation>(ProjectionLifecycle.Inline);
        options.Projections.Add<IncidentDetailsProjection>(ProjectionLifecycle.Inline);
        options.Projections.Add<IncidentShortInfoProjection>(ProjectionLifecycle.Inline);
        options.Projections.Add<CustomerIncidentsSummaryProjection>(ProjectionLifecycle.Async);
        options.Projections.Add(new KafkaProducer(builder.Configuration), ProjectionLifecycle.Async);
        options.Projections.Add(
            new SignalRProducer((IHubContext)sp.GetRequiredService<IHubContext<IncidentsHub>>()),
            ProjectionLifecycle.Async
        );

        return options;
    })
    .OptimizeArtifactWorkflow(TypeLoadMode.Static)
    .UseLightweightSessions()
    .AddAsyncDaemon(DaemonMode.Solo)
    // Add Marten/PostgreSQL integration with Wolverine's outbox
    .IntegrateWithWolverine();

builder.Services.AddResourceSetupOnStartup();

builder.Services
    .AddCors(options =>
        options.AddPolicy("ClientPermission", policy =>
            policy.WithOrigins("http://localhost:3000")
                .AllowAnyMethod()
                .AllowAnyHeader()
                .AllowCredentials()
        )
    )
    .Configure<JsonOptions>(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(o =>
        o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
    .AddSignalR();

builder.Host.ApplyOaktonExtensions();
// Configure Wolverine
builder.Host.UseWolverine();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger()
        .UseSwaggerUI();
}


app.UseCors("ClientPermission");
app.MapHub<IncidentsHub>("/hubs/incidents");

// Let's add in Wolverine HTTP endpoints to the routing tree
app.MapWolverineEndpoints();

return await app.RunOaktonCommands(args);

public class IncidentsHub: Hub
{
}

public partial class Program
{
}