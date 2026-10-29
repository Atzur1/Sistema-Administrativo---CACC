# HU-059 (code review): qué base puede recrear reset_base.ps1. Se incluye con dot-sourcing. Las comprobaciones de
# Assert-DestinoDePrueba son puras (no tocan SQL), así se pueden probar en negativo sin conectarse a nada; la de la marca
# (Assert-MarcaDePrueba) solo LEE la base.
#
# Antes alcanzaba con que el nombre terminara en "prueba" o "_e2e" sin distinguir mayúsculas: ClubCamionerosPRUEBA (la base A
# de origen) pasaba, y el nombre se insertaba sin escapar en el DROP DATABASE. Ahora, en este orden:
#  1. el nombre es un identificador simple (letras, dígitos y "_"), empieza con letra y termina EXACTAMENTE en "_Prueba" o
#     "_E2E" (distinguiendo mayúsculas): no puede cerrar el corchete ni agregar sentencias;
#  2. no es una base protegida (las de origen, las de trabajo y las del sistema), sin importar mayúsculas;
#  3. el servidor es local (LocalDB, localhost, 127.0.0.1 o ".") o está en CACC_E2E_SERVIDORES_PERMITIDOS (lista exacta,
#     separada por comas, para un servidor de pruebas compartido);
#  4. el entorno del proceso no es otro que Development o Testing (ASPNETCORE_ENVIRONMENT / DOTNET_ENVIRONMENT);
#  5. si la base ya existe, tiene la marca que deja reset_base.ps1 al crearla (propiedad extendida CACC_BaseDePrueba). Una
#     base con nombre de prueba que no creó este script NO se borra.

$script:PatronNombreDePrueba = '^[A-Za-z][A-Za-z0-9_]{2,100}_(Prueba|E2E)$'
$script:BasesProtegidas = @('ClubCamionerosPRUEBA', 'CACC_B_Origen', 'CCACUnificada', 'CCACUnificada_Antes', 'ClubCamioneros',
    'ClubCamionerosUnificadaV2', 'BD_Mantenimientos', 'master', 'model', 'msdb', 'tempdb')
$script:PatronServidorLocal = '^(\(localdb\)\\[A-Za-z0-9_.-]+|localhost|127\.0\.0\.1|\.)(\\[A-Za-z0-9_$]+)?(,\d{1,5})?$'
$script:MarcaDePrueba = 'CACC_BaseDePrueba'

function Assert-DestinoDePrueba([string]$Servidor, [string]$Base) {
    if ($Base -cnotmatch $script:PatronNombreDePrueba) {
        throw "La base '$Base' no es un nombre de prueba válido: solo letras, dígitos y '_', y tiene que terminar en '_Prueba' o '_E2E'. No se recrea."
    }
    if ($script:BasesProtegidas -contains $Base) {
        throw "La base '$Base' está protegida. No se recrea."
    }
    $permitidos = @(($env:CACC_E2E_SERVIDORES_PERMITIDOS -split ',') | ForEach-Object { $_.Trim() } | Where-Object { $_ })
    if ($Servidor -notmatch $script:PatronServidorLocal -and $permitidos -cnotcontains $Servidor) {
        throw "El servidor '$Servidor' no es local ni está en CACC_E2E_SERVIDORES_PERMITIDOS. No se recrea ninguna base."
    }
    foreach ($variable in 'ASPNETCORE_ENVIRONMENT', 'DOTNET_ENVIRONMENT') {
        $entorno = [Environment]::GetEnvironmentVariable($variable)
        if ($entorno -and $entorno -notin 'Development', 'Testing') {
            throw "${variable}=${entorno}: el reset solo corre en Development o Testing. No se recrea."
        }
    }
}

# Consulta de solo lectura. $Consultar recibe (base, consulta) y devuelve el texto; se pasa para poder probarlo sin SQL.
function Assert-MarcaDePrueba([string]$Base, [scriptblock]$Consultar, [switch]$AdoptarBaseSinMarca) {
    $existe = & $Consultar "master" "SELECT CASE WHEN DB_ID(N'$Base') IS NULL THEN 0 ELSE 1 END"
    if ($existe -ne "1") { return "nueva" }
    $marca = & $Consultar $Base "SELECT ISNULL(CONVERT(NVARCHAR(50), (SELECT value FROM sys.extended_properties WHERE class = 0 AND name = N'$($script:MarcaDePrueba)')), N'')"
    if ($marca -eq "reset_base") { return "marcada" }
    if ($AdoptarBaseSinMarca) { return "adoptada" }
    throw "La base '$Base' existe pero no la creó reset_base.ps1 (no tiene la marca $($script:MarcaDePrueba)). No se borra. " +
        "Si es una base de prueba anterior a esta protección, revisala y volvé a correr con -AdoptarBaseSinMarca."
}
