$ErrorActionPreference = 'Stop'
$manifest = Join-Path $PSScriptRoot 'com.ram.chrome_bridge.json'
$hostExe = Join-Path $PSScriptRoot 'RAM.ChromeBridge.exe'
if (-not (Test-Path -LiteralPath $hostExe)) { throw 'Missing RAM.ChromeBridge.exe' }
$data = @{ name='com.ram.chrome_bridge'; description='Roblox Account Manager Chrome bridge'; path=$hostExe; type='stdio'; allowed_origins=@('chrome-extension://mjapekjkfljcmpclkafajgmenailmkcn/') }
[IO.File]::WriteAllText($manifest, ($data | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
$key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey('Software\Google\Chrome\NativeMessagingHosts\com.ram.chrome_bridge')
try { $key.SetValue('', $manifest) } finally { $key.Dispose() }
Write-Output 'Chrome bridge registered for this Windows user. Load the ChromeExtension folder in chrome://extensions and enable Allow in incognito.'
