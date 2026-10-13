# Start and stop a disposable PostgreSQL instance for the repository tests.
# An explicitly supplied connection string must point to an isolated test database.

function Start-RepositoryPostgres {
    param([Parameter(Mandatory)][string] $ScratchRoot)

    if ($env:NYSSAAI_REPOSITORY_POSTGRES) {
        return [pscustomobject]@{ Kind = 'Borrowed'; ConnectionString = $env:NYSSAAI_REPOSITORY_POSTGRES }
    }

    $identity = [Guid]::NewGuid().ToString('N')
    $password = [Guid]::NewGuid().ToString('N')
    $containerName = "nyssaai-tests-$identity"
    $docker = Get-Command docker -ErrorAction SilentlyContinue
    if ($docker) {
        Write-Host 'Checking Docker availability for the PostgreSQL test fixture.'
        $dockerKind = $null
        try { $dockerKind = & $docker.Source info --format '{{.OSType}}' 2>$null }
        catch {
            # Windows PowerShell treats a stopped daemon's stderr as an error.
            # Continue to the installed PostgreSQL fallback when Docker is unavailable.
        }
        if ($LASTEXITCODE -eq 0 -and $dockerKind -eq 'linux') {
            try {
                & $docker.Source run --detach --name $containerName --publish '127.0.0.1::5432' `
                    --env POSTGRES_USER=nyssaai_test --env "POSTGRES_PASSWORD=$password" `
                    --env POSTGRES_DB=nyssaai_test postgres:17-alpine | Out-Null
                if ($LASTEXITCODE -ne 0) { throw 'Could not start the PostgreSQL test container.' }
                $portMapping = & $docker.Source port $containerName 5432/tcp
                if ($LASTEXITCODE -ne 0 -or $portMapping -notmatch '^127\.0\.0\.1:(\d+)$') {
                    throw 'Could not determine the PostgreSQL test port.'
                }
                $port = $Matches[1]
                $ready = $false
                for ($attempt = 0; $attempt -lt 60; $attempt++) {
                    & $docker.Source exec $containerName pg_isready -h 127.0.0.1 -U nyssaai_test -d nyssaai_test *> $null
                    if ($LASTEXITCODE -eq 0) { $ready = $true; break }
                    Start-Sleep -Seconds 1
                }
                if (-not $ready) { throw 'The PostgreSQL test container did not become ready.' }
                return [pscustomobject]@{
                    Kind = 'Docker'; Container = $containerName; Executable = $docker.Source
                    ConnectionString = "Host=127.0.0.1;Port=$port;Database=nyssaai_test;Username=nyssaai_test;Password=$password;Pooling=False"
                }
            }
            catch {
                & $docker.Source rm --force $containerName *> $null
                throw
            }
        }
    }

    # Hosted Windows runners ship PostgreSQL binaries; no existing service or database is reused.
    $postgresBin = $env:PGBIN
    if (-not $postgresBin) {
        $initdb = Get-Command initdb -ErrorAction SilentlyContinue
        if ($initdb) { $postgresBin = Split-Path -Parent $initdb.Source }
    }
    if (-not $postgresBin -and $env:ProgramFiles) {
        $postgresInstall = Join-Path $env:ProgramFiles 'PostgreSQL'
        if (Test-Path -LiteralPath $postgresInstall) {
            $postgresBin = Get-ChildItem -Path "$postgresInstall/*/bin/initdb.exe" |
                Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty DirectoryName
        }
    }
    if (-not $postgresBin) {
        throw 'Repository tests require Docker with Linux containers, PostgreSQL binaries on PATH/PGBIN, or NYSSAAI_REPOSITORY_POSTGRES pointing to an isolated test database.'
    }

    $workspace = Join-Path $ScratchRoot "postgres-$identity"
    New-Item -ItemType Directory -Path $workspace -Force | Out-Null
    $dataDirectory = Join-Path $workspace 'data'
    $passwordFile = Join-Path $workspace 'password'
    [IO.File]::WriteAllText($passwordFile, $password)
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 0)
    $listener.Start()
    $port = $listener.LocalEndpoint.Port
    $listener.Stop()
    $pgCtl = Join-Path $postgresBin 'pg_ctl'
    $serverLog = Join-Path $workspace 'server.log'
    try {
        Write-Host 'Initializing the temporary native PostgreSQL cluster.'
        & (Join-Path $postgresBin 'initdb') --pgdata=$dataDirectory --username=nyssaai_test `
            --auth-host=scram-sha-256 --auth-local=trust --pwfile=$passwordFile *> (Join-Path $workspace 'initdb.log')
        if ($LASTEXITCODE -ne 0) { throw "Could not initialize PostgreSQL. See $workspace/initdb.log." }
        Write-Host 'Starting the temporary native PostgreSQL server.'
        if ($env:OS -eq 'Windows_NT') {
            # Wait only for pg_ctl; PostgreSQL must keep running after its launcher exits.
            # A direct pipeline can remain attached to the server's inherited handles.
            $arguments = '--pgdata="{0}" --log="{1}" --options="-h 127.0.0.1 -p {2}" --timeout=30 --wait start' -f `
                $dataDirectory, $serverLog, $port
            $launcher = Start-Process -FilePath "$pgCtl.exe" -ArgumentList $arguments -WindowStyle Hidden -PassThru `
                -RedirectStandardOutput (Join-Path $workspace 'startup.log') `
                -RedirectStandardError (Join-Path $workspace 'startup-error.log')
            try {
                # Keep the process handle available for ExitCode on Windows PowerShell.
                $null = $launcher.Handle
                if (-not $launcher.WaitForExit(45000)) {
                    $launcher.Kill()
                    throw "PostgreSQL startup timed out. See $serverLog."
                }
                $startupExitCode = $launcher.ExitCode
            }
            finally { $launcher.Dispose() }
        }
        else {
            & $pgCtl --pgdata=$dataDirectory --log=$serverLog `
                --options="-h 127.0.0.1 -p $port" --timeout=30 --wait start *> (Join-Path $workspace 'startup.log')
            $startupExitCode = $LASTEXITCODE
        }
        if ($startupExitCode -ne 0) { throw "Could not start PostgreSQL. See $serverLog." }
        Write-Host 'The temporary native PostgreSQL server is ready.'
        return [pscustomobject]@{
            Kind = 'Native'; Executable = $pgCtl; DataDirectory = $dataDirectory
            ConnectionString = "Host=127.0.0.1;Port=$port;Database=postgres;Username=nyssaai_test;Password=$password;Pooling=False"
        }
    }
    catch {
        & $pgCtl --pgdata=$dataDirectory --mode=immediate --wait stop *> $null
        throw
    }
    finally {
        Remove-Item -LiteralPath $passwordFile -ErrorAction SilentlyContinue
    }
}

function Stop-RepositoryPostgres {
    param($Fixture)

    if ($null -eq $Fixture -or $Fixture.Kind -eq 'Borrowed') { return }
    if ($Fixture.Kind -eq 'Docker') {
        & $Fixture.Executable rm --force $Fixture.Container *> $null
    }
    elseif ($Fixture.Kind -eq 'Native') {
        & $Fixture.Executable --pgdata=$Fixture.DataDirectory --mode=immediate --wait stop *> $null
    }
    if ($LASTEXITCODE -ne 0) { Write-Warning 'The temporary PostgreSQL instance could not be stopped.' }
}
