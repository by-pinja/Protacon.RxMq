[![Build Status](https://dev.azure.com/Protacon/Protacon.RxMq/_apis/build/status/by-pinja.Protacon.RxMq?branchName=refs%2Fpull%2F32%2Fmerge)](https://dev.azure.com/Protacon/Protacon.RxMq/_build/latest?definitionId=19&branchName=refs%2Fpull%2F32%2Fmerge)
[![Nuget](https://img.shields.io/nuget/dt/Protacon.RxMq.Abstractions.svg)](https://www.nuget.org/packages/Protacon.RxMq.Abstractions/)
[![Nuget](https://img.shields.io/nuget/dt/Protacon.RxMq.AzureServiceBus.svg)](https://www.nuget.org/packages/Protacon.RxMq.AzureServiceBus/)

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

Contains modern .NET Core version and legacy (4.8) version with full framework.

## Configuring

Configure `AzureQueueMqSettings`, there are configuration methods which can be overwritten if required. Common reason for configuring factory methods are

* Multitenancy (with topics and subscription filters)
* Custom behavior how topic, subscription and queue names are built.

## Developing

Requires NET core 2.x. and .NET Framework SDK 4.8

Setting up test environment

1. Create Application in Azure Active Directory
1. Create configuration files
    1. Create a copy of `developer-settings.example.json` with name `developer-settings.json`
    1. Replace `developer-settings.json` content with correct values
1. Create testing environment
    1. Replace "ResourceGroupHere" with the actual name of the RG
    1. Run `Testing/Prepare-Environment.ps1 -EnvironmentName "ResourceGroupHere"` (see script for detailed
    documentation)
1. Create client secrets
    1. 1. Replace "ResourceGroupHere" with the actual name of the RG
    1. Run `Create-ClientSecrets.ps1 -EnvironmentName "ResourceGroupHere"`

After running these steps, you should have a Service Bus in Azure and
`client-secrets.json` -file for testing. Environment variables can also be used
for configurations. See `AzureMqSettingsBase` for required configurations.

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

## Tests

To be able to run tests

1. login to Azure with command line (Az login)
1. in root create developer-settings.json file for configuring test environment (Azure rg with service bus). Check developer-settings-example.json for reference
   (tag is vf subproject for devops if environment is created into pinja dev subscription)
1. run in Testing folder

    ```poweshell
    Prepare-Testenvironment -SettingsFile developer-settings.json
    ```

1. In root run

    ```powershell
    Create-setcets.ps1 -SettingsFile developer-settings.json
    ```

    This file creates secrets for test settings (dont commit)
1. run tests in core and legacy projects

## Creating and publishing nuget packages by hand if needed

note: You must have nuget credentials added to 'Protacon Nuget packages' repo to be able to do next steps

1. Make new release in Github with next version number and add description what is being changed
1. Create folder artifacts in same forlder where csproj to be published is located
1. In rootfolder run:

    ```bash
    dotnet pack .\Protacon.RxMq.PLACEPROJECTFOLDERHERE\Protacon.RxMq.PLACEPROJECTHERE.csproj -c Release -o .\Protacon.RxMq.PLACEPROJECTFOLDERHERE\artifacts /p:Version=x.x.x 
    ```

    (version same as github release number)
1. Create ApiKey in Nuget Gallery (owner Protacon, select RxMq packages)
1. In artifacts folder publish to nuget.org:

    ```bash
    dotnet nuget push Protacon.RxMq.PACKETPROJECT.x.x.x.nupkg --api-key YOURAPIKEY --source https://api.nuget.org/v3/index.json
    ```

## CI

Project uses Azure devops pipeline <https://dev.azure.com/Protacon/Protacon.RxMq>
Pipeline is authorized with service principal read from devops secret file
Nuget api key is devops pipeline variable $(nugetApiKey) create new in nuget org in your account if needed
It publishes nugets to <https://www.nuget.org/packages?q=rxmq>
Pipeline tag build triggers when new release with tag is made in github  
Pipeline flow : build-create env for tests with secrets - run test -tear down env - if tag (release) publish nugets  
