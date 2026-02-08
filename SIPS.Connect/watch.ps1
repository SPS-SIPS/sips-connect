<#
Windows equivalent of watch.sh
- Loads environment variables from a local .env file into the current process
- Runs `dotnet watch` (pass-through args supported)

Usage:
  ./watch.ps1
  ./watch.ps1 -EnvFile .env
  ./watch.ps1 -- run --project ./SIPS.Connect.sln

Notes:
- This sets variables only for the current PowerShell process.
- Lines beginning with # are ignored.
- Supports KEY=VALUE lines; optional surrounding quotes are trimmed.
#>

[CmdletBinding(PositionalBinding = $false)]
param(
  [string]$EnvFile = ".env"
)

function Set-DotEnvVars([string]$Path) {
  if (-not (Test-Path -LiteralPath $Path)) {
    Write-Verbose "No .env found at '$Path' (skipping)."
    return
  }

  Get-Content -LiteralPath $Path | ForEach-Object {
    $line = $_.Trim()

    if ([string]::IsNullOrWhiteSpace($line)) { return }
    if ($line.StartsWith('#')) { return }

    # Allow optional leading `export ` (common in bash .env files)
    if ($line.StartsWith('export ')) {
      $line = $line.Substring(7).Trim()
    }

    $idx = $line.IndexOf('=')
    if ($idx -lt 1) { return }

    $name = $line.Substring(0, $idx).Trim()
    $value = $line.Substring($idx + 1).Trim()

    # Strip optional surrounding quotes
    if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
      $value = $value.Substring(1, $value.Length - 2)
    }

    if (-not [string]::IsNullOrWhiteSpace($name)) {
      Set-Item -Path ("Env:{0}" -f $name) -Value $value
    }
  }
}

Set-DotEnvVars -Path $EnvFile

# Pass through any extra args to dotnet watch
& dotnet watch @args
exit $LASTEXITCODE
