# FIX turn-on-microsoft-update: register the Microsoft Update service with Windows Update (flags 7 = allow
# pending registration, allow online registration, register with AU). Returns plain values.
$sm = New-Object -ComObject Microsoft.Update.ServiceManager
$sm.AddService2('7971f918-a847-4430-9279-4a52d1efe18d', 7, '') | Out-Null
$on = [bool]($sm.Services | Where-Object { $_.ServiceID -eq '7971f918-a847-4430-9279-4a52d1efe18d' -and $_.IsRegisteredWithAU })
[pscustomobject]@{ Result = if ($on) { 'Microsoft Update is on: Office and other Microsoft products will be patched with Windows.' } else { 'Registration did not take: check Windows Update policy.' } }
