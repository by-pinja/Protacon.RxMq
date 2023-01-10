param(
    [Parameter()][string]$SettingsFile = 'developer-settings.json',
    [Parameter(Mandatory)][string]$EnvironmentName
)
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

Write-Host "Reading settings from file $SettingsFile"
$settingsJson = Get-Content -Raw -Path $SettingsFile | ConvertFrom-Json

$context = Get-AzContext

$busKey = Get-AzServiceBusKey -ResourceGroupName $EnvironmentName -Namespace $EnvironmentName -Name 'RootManageSharedAccessKey'

$output = @{
    ConnectionString         = $busKey.PrimaryConnectionString
    AzureResourceGroup       = $EnvironmentName
    AzureSubscriptionId      = $context.Subscription.Id
    AzureSpAppId             = $settingsJson.ServicePrincipal.AppId
    AzureSpPassword          = $settingsJson.ServicePrincipal.Password
    AzureSpTenantId          = $context.Tenant.Id
    AzureNamespace           = $EnvironmentName
    AzureRetryMinimumBackoff = 5
    AzureRetryMaximumBackoff = 10
    AzureMaximumRetryCount   = 10
}

$filename = "client-secrets.json"
$projectLocation = "Protacon.RxMq.AzureServiceBus.Tests"
$output | ConvertTo-Json -depth 100 | Out-File "$projectLocation\$filename"

Write-Host "Write file '$filename' to '$projectLocation'"
