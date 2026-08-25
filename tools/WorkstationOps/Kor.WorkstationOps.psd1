@{
    RootModule        = 'Kor.WorkstationOps.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'b7f4e2a1-5c3d-4e8b-9a1f-2d6c8e4b7a30'
    Author            = 'Ian Lalonde'
    CompanyName       = 'KOR Structural'
    Description       = 'Remote diagnostics and remediation for KOR workstations over c$ and sc.exe, for a network where WinRM and RPC dynamic ports are blocked.'
    PowerShellVersion = '7.0'
    FunctionsToExport = @(
        'Test-KorWorkstationChannel'
        'Use-KorRemoteRegistry'
        'Get-KorWorkstationEvent'
        'Get-KorOutlookAddin'
        'Get-KorOutlookStore'
        'Get-KorOfficeHealth'
        'Set-KorOutlookAddinState'
        'Invoke-KorSearchIndexRebuild'
        'Get-KorWorkstationHealth'
        'Get-KorServiceState'
        'Wait-KorServiceState'
        'Get-KorHardwareProfile'
        'ConvertFrom-KorSmbios'
        'Get-KorThermalProfile'
    )
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = @()
    PrivateData       = @{ PSData = @{ Tags = @('KOR', 'Workstation', 'Outlook', 'Office', 'Diagnostics') } }
}
