$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Ejecución puntual auditada: realizar y verificar un respaldo antes de usarla.
# El historial y los hashes respetan el contrato del ejecutor de la API.
$carpeta = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../migrations'))
$permitidos = @('V20260923_01__crear_aranceles.sql', 'V20260923_02__index_pagos_pendientes.sql')
if (!(Test-Path -LiteralPath $carpeta -PathType Container)) { throw 'Falta la carpeta de migraciones.' }
if ((Get-Item -LiteralPath $carpeta).Attributes -band [IO.FileAttributes]::ReparsePoint) {
    throw 'La carpeta de migraciones no puede ser un enlace.'
}
$archivos = @(Get-ChildItem -LiteralPath $carpeta -Force)
foreach ($archivo in $archivos) {
    if ($archivo.PSIsContainer -or ($archivo.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw 'No se admiten directorios ni enlaces en migraciones.'
    }
    if ($archivo.Name -cne '.gitkeep' -and $archivo.Name -cnotin $permitidos) {
        throw "Archivo no autorizado: $($archivo.Name)"
    }
}
$scripts = @($archivos | Where-Object Name -CIn $permitidos | Sort-Object Name)
if ($scripts.Count -ne $permitidos.Count) { throw 'Faltan migraciones de la actualización auditada.' }
$porNombre = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
$versiones = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($archivo in $scripts) {
    $nombre = $archivo.Name
    $coincidencia = [regex]::Match($nombre, '^V(?<version>\d{8}_\d{2})__[a-z0-9_]+\.sql$')
    if (!$coincidencia.Success -or $nombre.Length -gt 200) { throw "Nombre inválido: $nombre" }
    $version = $coincidencia.Groups['version'].Value
    if (!$versiones.Add($version)) { throw "Versión duplicada: $version" }
    $sql = [IO.File]::ReadAllText($archivo.FullName).TrimStart([char]0xFEFF).Replace("`r`n", "`n").Replace("`r", "`n")
    if ([string]::IsNullOrWhiteSpace($sql) -or [regex]::IsMatch($sql, '(?im)^\s*(GO\b|:|!!)') -or
        $sql.Contains('$(') -or [regex]::IsMatch($sql, '(?i)\b(COMMIT|ROLLBACK|USE)\b|\b(BEGIN|SAVE)\s+(DISTRIBUTED\s+)?TRAN(SACTION)?\b|\bSET\s+(NOEXEC|PARSEONLY|IMPLICIT_TRANSACTIONS|XACT_ABORT)\b')) {
        throw "SQL no admitido por el ejecutor: $nombre"
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $hash = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($sql))).Replace('-', '') }
    finally { $sha.Dispose() }
    $porNombre.Add($nombre, [pscustomobject]@{ Nombre = $nombre; Version = $version; Sql = $sql; Hash = $hash })
}

$opciones = [System.Data.SqlClient.SqlConnectionStringBuilder]::new(
    'Data Source=(localdb)\MSSQLLocalDB;Initial Catalog=ClubCamionerosPRUEBA;Integrated Security=True;TrustServerCertificate=True;Pooling=False')
$db = [System.Data.SqlClient.SqlConnection]::new($opciones.ConnectionString)
try {
    $db.Open()
    $comando = $db.CreateCommand()
    try {
        $comando.CommandTimeout = 70
        $comando.CommandText = @'
DECLARE @resultado int;
EXEC @resultado = sys.sp_getapplock @Resource = N'CACC:Migraciones',
    @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 60000;
SELECT @resultado;
'@
        if ([int]$comando.ExecuteScalar() -lt 0) { throw 'No se pudo obtener el bloqueo de migraciones.' }
        $comando.CommandText = @'
SET XACT_ABORT ON;
IF OBJECT_ID(N'dbo.__CaccMigraciones', N'U') IS NULL
CREATE TABLE dbo.__CaccMigraciones (
    Nombre nvarchar(200) NOT NULL PRIMARY KEY,
    Hash char(64) NOT NULL,
    AplicadaEnUtc datetime2 NOT NULL DEFAULT SYSUTCDATETIME()
);
'@
        [void]$comando.ExecuteNonQuery()
        $historial = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
        $comando.CommandText = 'SELECT Nombre, Hash FROM dbo.__CaccMigraciones;'
        $lector = $comando.ExecuteReader()
        try { while ($lector.Read()) { $historial.Add($lector.GetString(0), $lector.GetString(1)) } }
        finally { $lector.Dispose() }
        foreach ($nombre in $historial.Keys) {
            if (!$porNombre.ContainsKey($nombre)) { throw "Migración aplicada ausente: $nombre" }
            if (![string]::Equals($porNombre[$nombre].Hash, $historial[$nombre], [StringComparison]::OrdinalIgnoreCase)) {
                throw "Migración aplicada modificada: $nombre"
            }
        }
        $ordenadas = @($porNombre.Values | Sort-Object Version)
        $aplicadas = @($ordenadas | Where-Object { $historial.ContainsKey($_.Nombre) })
        $pendientes = @($ordenadas | Where-Object { !$historial.ContainsKey($_.Nombre) })
        if ($aplicadas.Count -gt 0) {
            foreach ($script in $pendientes) {
                if ([string]::CompareOrdinal($script.Version, $aplicadas[-1].Version) -lt 0) {
                    throw "Migración pendiente fuera de orden: $($script.Nombre)"
                }
            }
        }
        foreach ($script in $pendientes) {
            $transaccion = $db.BeginTransaction()
            try {
                $comando.Transaction = $transaccion
                $comando.CommandTimeout = 120
                $comando.Parameters.Clear()
                $comando.CommandText = $script.Sql
                [void]$comando.ExecuteNonQuery()
                $comando.CommandText = 'INSERT INTO dbo.__CaccMigraciones (Nombre, Hash) VALUES (@nombre, @hash);'
                $comando.Parameters.Add('@nombre', [Data.SqlDbType]::NVarChar, 200).Value = $script.Nombre
                $comando.Parameters.Add('@hash', [Data.SqlDbType]::Char, 64).Value = $script.Hash
                [void]$comando.ExecuteNonQuery()
                $transaccion.Commit()
                Write-Output "Migración aplicada: $($script.Nombre)"
            }
            finally {
                # Dispose revierte la transacción si no llegó a confirmarse.
                $transaccion.Dispose()
                $comando.Transaction = $null
            }
        }
        Write-Output "Migraciones aplicadas: $($pendientes.Count)"
    }
    finally { $comando.Dispose() }
}
finally {
    # Sin pool: cerrar la sesión libera también su bloqueo de aplicación.
    $db.Dispose()
}
