function Select-GameDirectory {
    if (-not [Environment]::UserInteractive -or
        @([Environment]::GetCommandLineArgs() | Where-Object { $_ -match '^[-/](noninteractive|noni)$' }).Count -gt 0) {
        throw 'Folder selection is unavailable in noninteractive mode. Supply -GameDir explicitly.'
    }
    if ([Threading.Thread]::CurrentThread.GetApartmentState() -ne [Threading.ApartmentState]::STA) {
        throw 'Folder selection requires an STA PowerShell session. Start PowerShell with -STA or supply -GameDir explicitly.'
    }

    Add-Type -AssemblyName System.Windows.Forms
    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
    try {
        $dialog.Description = 'Select the Beat Saber 1.46.0 installation folder.'
        $dialog.ShowNewFolderButton = $false
        if ($dialog.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) {
            throw 'Folder selection canceled. Nothing was changed.'
        }
        return $dialog.SelectedPath
    } finally {
        $dialog.Dispose()
    }
}
