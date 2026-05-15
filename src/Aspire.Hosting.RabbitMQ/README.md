# Aspire.Hosting.RabbitMQ library

Provides extension methods and resource definitions for an Aspire AppHost to configure a RabbitMQ resource.

## Getting started

### Install the package

In your AppHost project, install the Aspire RabbitMQ Hosting library with [NuGet](https://www.nuget.org):

```dotnetcli
dotnet add package Aspire.Hosting.RabbitMQ
```

## Usage example

Then, in the _AppHost.cs_ file of `AppHost`, add a RabbitMQ resource and consume the connection using the following methods:

```csharp
var rmq = builder.AddRabbitMQ("rmq");

var myService = builder.AddProject<Projects.MyService>()
                       .WithReference(rmq);
```

## Connection Properties

When you reference a RabbitMQ resource using `WithReference`, the following connection properties are made available to the consuming project:

### RabbitMQ server

The RabbitMQ server resource exposes the following connection properties:

| Property Name | Description |
|---------------|-------------|
| `Host` | The hostname or IP address of the RabbitMQ server |
| `Port` | The port number the RabbitMQ server is listening on |
| `Username` | The username for authentication |
| `Password` | The password for authentication |
| `Uri` | The connection URI, with the format `amqp://{Username}:{Password}@{Host}:{Port}` |

Aspire exposes each property as an environment variable named `[RESOURCE]_[PROPERTY]`. For instance, the `Uri` property of a resource called `db1` becomes `DB1_URI`.

## Queue declaration requirements (RabbitMQ 4.3+)

Starting with RabbitMQ 4.3 — the default image used by `AddRabbitMQ` — the deprecated `transient_nonexcl_queues` feature is disabled by default. Declaring a queue that is simultaneously non-durable (transient) and non-exclusive is rejected by the broker with AMQP error 541 (`INTERNAL_ERROR - Feature 'transient_nonexcl_queues' is deprecated.`).

Declare queues as one of the following instead:

- **Durable** (`durable: true`) — queue and its definition survive broker restarts.
- **Transient exclusive** (`exclusive: true`) — queue is auto-deleted when the declaring connection closes; suited to per-connection ephemeral queues.
- **Durable with a queue TTL** — for short-lived queues whose definition survives restart but whose contents expire.

See the [RabbitMQ 4.3.0 release notes](https://github.com/rabbitmq/rabbitmq-server/releases/tag/v4.3.0) for upstream guidance.

## Additional documentation

* https://aspire.dev/integrations/messaging/rabbitmq/

## Feedback & contributing

https://github.com/microsoft/aspire
