$key = 'Software\Google\Chrome\NativeMessagingHosts\com.ram.chrome_bridge'
$registered = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($key)
if ($null -ne $registered) {
    $path = $registered.GetValue('')
    $registered.Dispose()
    if ($path -eq (Join-Path $PSScriptRoot 'com.ram.chrome_bridge.json')) {
        [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($key, $false)
    }
}
Write-Output 'Remove RAM Login Helper in chrome://extensions to finish uninstalling.'
