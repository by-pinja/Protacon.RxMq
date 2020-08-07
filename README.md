[![Build status](https://ci.appveyor.com/api/projects/status/2bje1v2br53g8377?svg=true)](https://ci.appveyor.com/project/savpek/protacon-rxmq)
[![Nuget](https://img.shields.io/nuget/dt/Protacon.RxMq.Abstractions.svg)](https://www.nuget.org/packages/Protacon.RxMq.Abstractions/)
[![Nuget](https://img.shields.io/nuget/dt/Protacon.RxMq.AzureServiceBus.svg)](https://www.nuget.org/packages/Protacon.RxMq.AzureServiceBus/)
[![Nuget](https://img.shields.io/nuget/dt/Protacon.RxMq.AzureServiceBusLegacy.svg)](https://www.nuget.org/packages/Protacon.RxMq.AzureServiceBusLegacy/)

# RxMq

Abstraction over common MQ channels with Rx.NET. Provides customizable messaging between clients and offers Observable endpoints for both topics and queues.

## Messages

Example message implementation (`ITopic` mechanic can be overwritten, it's just default behavior.)
```csharp
public class TestMessageForTopic: ITopic
{
    public Guid ExampleId { get; set; }
    public string Something { get; set; }
    public string TopicName => "v1.testtopic";
}
```

## Sending

Sending message

```csharp
await publisher.SendAsync(new TestMessage
{
    ExampleId = id
});
```

## Receiving

```csharp
subscriber.Messages<TestMessage>().Subscribe(message => Console.WriteLine(x.ExampleId));
```

# AzureServiceBus

Abstraction over Azure Service Bus.

Contains modern .NET Core version and legacy (pre 4.7.2) version with full framework.

## Configuring

Configure `AzureQueueMqSettings`, there are configuration methods which can be overwritten if required. Common reason for configuring factory methods are

* Multitenancy (with topics and subscription filters)
* Custom behavior how topic, subscription and queue names are built.

## Developing

Requires NET core 2.x. and .NET Framework SDK 4.5 and 4.6.1

Setup settings required in `AzureMqSettingsBase` with environment variables or client-secrets.json file.

```bash
dotnet restore
dotnet test
```

NOTE: If build doesn't find correct SDK's even if those are installed,
please verify that `ReferenceAssemblyRoot` is defined and set to correct location

In powershell, this can be done with

```powershell
$env:ReferenceAssemblyRoot = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework'
```
