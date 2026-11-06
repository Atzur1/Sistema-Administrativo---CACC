param([string]$Base='HU064_Revalidacion_20261104',[string]$AdminUrl='http://localhost:5328',[string]$SportsUrl='http://localhost:5329',[string]$Run='principal')
if($Base -notmatch '^HU064_Revalidacion_[0-9]{8}$' -or $AdminUrl -ne 'http://localhost:5328' -or $SportsUrl -ne 'http://localhost:5329'){throw 'Only isolated HU064 database and ports 5328/5329 allowed'}
$ErrorActionPreference='Stop'
$original='C:\Users\FramD\Documents\Tesis\HU-064-entorno'
$old=Get-Content "$original\hu064-e2e.ps1" -Raw
$PW=[regex]::Match($old,"\`$PW = '([^']+)'").Groups[1].Value
if(!$PW){throw 'Missing password'}
$key=(Get-Content "$original\cacc-unificacion\trabajo\nuestro\ApiGestion\appsettings.Development.json" -Raw|ConvertFrom-Json).Integracion.ClienteDeportivo.Clave
if(!$key){throw 'Missing integration key'}
$script:runDnis=@(); $results=[Collections.Generic.List[object]]::new(); $traffic=[Collections.Generic.List[object]]::new()
function Now { [DateTime]::UtcNow.ToString('o') }
function Call($method,$url,$token,$body=$null,$integration=$false){
 $headers=@{};if($token){$headers.Authorization="Bearer $token"};if($integration){$headers['X-Integration-Key']=$key}
 $p=@{Method=$method;Uri=$url;Headers=$headers;UseBasicParsing=$true;TimeoutSec=90};if($null-ne$body){$p.Body=ConvertTo-Json -InputObject $body -Depth 10 -Compress;$p.ContentType='application/json; charset=utf-8'}
 $start=Now
 try{$r=Invoke-WebRequest @p;$s=[int]$r.StatusCode;$content=[string]$r.Content}catch{$s=0;$content=$_.ErrorDetails.Message;if($_.Exception.Response){$s=[int]$_.Exception.Response.StatusCode;try{$sr=New-Object IO.StreamReader($_.Exception.Response.GetResponseStream());$content=$sr.ReadToEnd()}catch{}};if(!$content){$content=$_.Exception.Message}}
 $end=Now
 if($url -match '/api/jugadores\?'){$content=ConvertTo-Json -InputObject @(($content|ConvertFrom-Json)|Where-Object {$_.dni -in $script:runDnis}) -Depth 20 -Compress}
 $safeBody=$body;$safeContent=$content
 if($url-match'/auth/login'){$safeBody=@{username=$body.username;password='[REDACTED]'};$safeContent='[REDACTED AUTH RESPONSE]'}
 $traffic.Add([ordered]@{start=$start;end=$end;method=$method;url=$url;request=$safeBody;status=$s;response=$safeContent})
 return @{s=$s;b=$content;start=$start;end=$end}
}
function SQL($query){
 $c=New-Object System.Data.SqlClient.SqlConnection("Server=(localdb)\MSSQLLocalDB;Database=$Base;Integrated Security=True;TrustServerCertificate=True")
 try{$c.Open();$cmd=$c.CreateCommand();$cmd.CommandText=$query;$reader=$cmd.ExecuteReader();$sb=[Text.StringBuilder]::new();while($reader.Read()){[void]$sb.Append($reader.GetValue(0).ToString())};$reader.Close();return $sb.ToString()}finally{$c.Dispose()}
}
function Login($dni){$r=Call POST "$AdminUrl/api/auth/login" $null @{username=$dni;password=$PW};if($r.s-ne200){throw "Login HTTP $($r.s)"};return ($r.b|ConvertFrom-Json).token}
$SA=Login '20111222';$DT=Login '30000004'
function State($id,$dni){
 $f=Call GET "$SportsUrl/api/jugadores/$id" $DT;$l=Call GET "$SportsUrl/api/jugadores?categoriaId=3" $DT;$s=Call GET "$AdminUrl/api/integracion/v1/estado-deuda/dni/$dni" $SA $null $true
 if($f.s-ne200-or$l.s-ne200-or$s.s-ne200){throw "State retrieval failed $($f.s)/$($l.s)/$($s.s)"}
 $item=@(($l.b|ConvertFrom-Json)|Where-Object dni -eq $dni);if($item.Count-ne1){throw "Expected one category player $dni"}
 return @{individual=($f.b|ConvertFrom-Json).estadoCuota;category=$item[0].estadoCuota;source=($s.b|ConvertFrom-Json).estado;f=$f;l=$l;s=$s}
}
function Snap($id){
 $raw=SQL "SELECT * FROM PAGOS WHERE FK_id_jugador=$id ORDER BY PK_id_pago FOR JSON PATH,INCLUDE_NULL_VALUES"
 $account=Call GET "$AdminUrl/api/pagos/jugador/$id/estado-de-cuenta" $SA
 if($account.s-ne200){throw "Account HTTP $($account.s)"}
 $anulled=SQL "SELECT * FROM PAGOS_ANULADOS WHERE FK_id_jugador=$id ORDER BY id_pago FOR JSON PATH,INCLUDE_NULL_VALUES"; $rows=ConvertFrom-Json -InputObject $raw; if($null-eq$rows){$rows=@()}; return @{annulled=$anulled;payments=$rows;rawPayments=$raw;account=($account.b|ConvertFrom-Json);rawAccount=$account.b}
}
function Require($test,$message){if(!$test){throw $message}}
$hoy=[TimeZoneInfo]::ConvertTimeBySystemTimeZoneId([DateTime]::UtcNow,'Argentina Standard Time').Date
$current=Get-Date -Year $hoy.Year -Month $hoy.Month -Day 1
$unexpired=$current.AddMonths(-1);$p1=$current.AddMonths(-2);$p2=$current.AddMonths(-3);$p3=$current.AddMonths(-4)
function Date($d){$d.ToString('yyyy-MM-dd')}
function Fixture($dni,$name,$periods){
 $script:runDnis+=$dni
 $r=Call POST "$SportsUrl/api/jugadores" $SA @{dni=$dni;nombre='HU064';apellido=$name;fechaNacimiento='2011-03-03';genero='Masculino';categoria='3';posicion='Volante'}
 Require ($r.s-in@(200,201)) "Fixture creation HTTP $($r.s): $($r.b)"
 $id=[int](SQL "SELECT j.PK_id_jugador FROM JUGADORES j JOIN PERSONAS p ON p.PK_id_persona=j.FK_id_persona WHERE p.dni='$dni'")
 Require ($id-gt0) 'Fixture missing'
 [void](SQL "DELETE FROM PAGOS WHERE FK_id_jugador=$id; SELECT 1")
 foreach($period in $periods){[void](SQL "INSERT PAGOS(FK_id_jugador,monto_base,monto_final,fecha_vencimiento,estado,concepto) VALUES($id,85000,85000,'$(Date $period)',0,'Cuota'); SELECT 1")}
 Require (@((Snap $id).payments).Count-eq@($periods).Count) "Fixture payment count mismatch id=$id actual=$(@((Snap $id).payments).Count) expected=$(@($periods).Count) periods=$periods"
 return $id
}
$mapping=@{habilitado='AL DÍA';solo_entrenamientos='SOLO ENTRENAMIENTOS';inhabilitado='INHABILITADO'}
function CheckState($state,$expected){return ($state.source-eq$expected-and$state.individual-eq$mapping[$expected]-and$state.category-eq$mapping[$expected])}
function Case($name,$id,$dni,$initial,$expected,$method,$path,$body,$status,$kind,$amount=0){
 $before=Snap $id;$ini=State $id $dni;Require (CheckState $ini $initial) "Initial state mismatch $name"
 $op=Call $method "$AdminUrl$path" $SA $body
 # The next operation after admin response is the individual sports query. No sleep/cache clear/restart/job.
 $afterState=State $id $dni;$after=Snap $id
 $asserts=[ordered]@{adminHttp=($op.s-eq$status);initialState=$true;individual=($afterState.individual-eq$mapping[$expected]);category=($afterState.category-eq$mapping[$expected]);officialSource=($afterState.source-eq$expected)}
 $paidBefore=@($before.payments|Where-Object estado -eq 1);$paidAfter=@($after.payments|Where-Object estado -eq 1)
 switch($kind){
 'payment' {$asserts.exactPaidCount=($paidAfter.Count-eq$paidBefore.Count+@($body.idsPago).Count);$asserts.exactPaidIds=(@($after.payments|Where-Object {$_.PK_id_pago -in $body.idsPago -and $_.estado-eq1}).Count-eq@($body.idsPago).Count);$asserts.persistedPaidIncrease=$paidAfter.Count-gt$paidBefore.Count;$asserts.accountDebtReduction=([decimal]$before.account.totalDebtAmount-[decimal]$after.account.totalDebtAmount)-eq$amount}
 'partial' {$asserts.abonoPersisted=($paidAfter.Count-eq$paidBefore.Count+1);$asserts.exactAbono=(@($paidAfter|Where-Object {[decimal]$_.monto_final-eq$amount}).Count-eq1);$asserts.remainingBalance=([decimal]$after.account.totalDebtAmount-eq45000)}
 'unchanged' {$asserts.fullPaymentsRollback=$before.rawPayments-ceq$after.rawPayments;$asserts.fullAccountUnchanged=$before.rawAccount-ceq$after.rawAccount;$asserts.annulledUnchanged=$before.annulled-ceq$after.annulled}
 'generation' {$asserts.newPayment=$after.payments.Count-eq$before.payments.Count+1;$asserts.debtIncreased=[decimal]$after.account.totalDebtAmount-gt[decimal]$before.account.totalDebtAmount}
 'annul' {$asserts.accountDebtRestored=([decimal]$after.account.totalDebtAmount-[decimal]$before.account.totalDebtAmount)-eq85000;$asserts.paymentsChanged=$before.rawPayments-cne$after.rawPayments;$asserts.annulmentAuditCreated=$before.annulled-cne$after.annulled}
 'benefit' {$asserts.zeroDebt=[decimal]$after.account.totalDebtAmount-eq0;$asserts.covered=@($after.account.fees|Where-Object coveredByBenefit -eq $true).Count-gt0}
 }
 $verdict=if(@($asserts.Values|Where-Object{!$_}).Count){'FAIL'}elseif($name-eq'beca-exacta'){'BLOCKED'}else{'PASS'}
 $row=[ordered]@{case=$name;expectedInitial=$initial;expectedObserved=$expected;acceptanceExpected=$(if($name-eq'beca-exacta'){'habilitado'}else{$expected});result=$verdict;playerId=$id;dni=$dni;assertions=$asserts;before=$before;after=$after;initial=$ini;observed=$afterState;operationStart=$op.start;adminResponse=$op.end;commitAt=$null;commitMeasurement='Not directly correlated; fecha_hora_registro is INSERT record time, not COMMIT';sportsQueryStart=$afterState.f.start;sportsResponse=$afterState.f.end;listStart=$afterState.l.start;listEnd=$afterState.l.end;individualReflectionMs=([datetime]$afterState.f.end-[datetime]$afterState.f.start).TotalMilliseconds;categoryReflectionMs=([datetime]$afterState.l.end-[datetime]$afterState.l.start).TotalMilliseconds;adminStatus=$op.s;adminBody=$op.b}
 $results.Add($row);Write-Host "$name $verdict HTTP $($op.s)";Save
}
function Save { @{run=$Run;database=$Base;generatedAt=Now;cases=@($results.ToArray());traffic=@($traffic.ToArray())}|ConvertTo-Json -Depth 50|Set-Content (Join-Path $PSScriptRoot "results-$Run.tmp.json") -Encoding utf8; Move-Item -LiteralPath (Join-Path $PSScriptRoot "results-$Run.tmp.json") -Destination (Join-Path $PSScriptRoot "results-$Run.json") -Force }
try{
 $suffix=Get-Random -Minimum 10000 -Maximum 99999;$fixtures=@{}
 $definitions=@{A=@($p2,$p1);B=@($p1);C=@($p3,$p2,$p1);P=@($p1);D=@($p1);N=@($unexpired);E=@();F=@($p2,$p1)}
 $index=0;foreach($name in @('A','B','C','P','D','N','E','F')){$index++;$dni="6$suffix$($index.ToString('00'))";$fixtures[$name]=@{id=(Fixture $dni "${Run}$name" $definitions[$name]);dni=$dni}}
 $e=$fixtures.E;[void](SQL "UPDATE JUGADORES SET fecha_alta='$(Date $p3)' WHERE PK_id_jugador=$($e.id); IF NOT EXISTS(SELECT 1 FROM ARANCELES WHERE genero='Masculino' AND FK_id_categoria IS NULL AND vigente_desde<='$(Date $p3)') INSERT ARANCELES(genero,monto,vigente_desde) VALUES('Masculino',85000,'2026-01-01'); SELECT 1")
 function Pending($id){return @((Snap $id).payments|Where-Object estado -eq 0|Sort-Object fecha_vencimiento|ForEach-Object{[int]$_.PK_id_pago})}
 $a=$fixtures.A;$ids=Pending $a.id;Case '2-a-1' $a.id $a.dni inhabilitado solo_entrenamientos POST '/api/pagos/cobro' @{idJugador=$a.id;idsPago=@($ids[0]);metodoPago='Efectivo'} 200 payment 85000
 $c=$fixtures.C;$ids=Pending $c.id;Case '3-a-0' $c.id $c.dni inhabilitado habilitado POST '/api/pagos/cobro' @{idJugador=$c.id;idsPago=@($ids);metodoPago='Transferencia'} 200 payment 255000
 $b=$fixtures.B;$ids=Pending $b.id;Case '1-a-0' $b.id $b.dni solo_entrenamientos habilitado POST '/api/pagos/cobro' @{idJugador=$b.id;idsPago=@($ids);metodoPago='Efectivo'} 200 payment 85000
 $p=$fixtures.P;$month=[Globalization.CultureInfo]::GetCultureInfo('es-AR').DateTimeFormat.GetMonthName($p1.Month);Case 'parcial' $p.id $p.dni solo_entrenamientos solo_entrenamientos POST '/api/pagos/registrar' @{idJugador=$p.id;periodo=$month;anio=$p1.Year;monto=40000;metodoPago='Efectivo'} 200 partial 40000
 $d=$fixtures.D;Case 'beca-exacta' $d.id $d.dni solo_entrenamientos solo_entrenamientos POST "/api/players/$($d.id)/discount" @{reason='Becado';valueType='%';percentage=100;startDate=(Date $p1);endDate=(Date $current.AddMonths(2))} 422 unchanged
 $n=$fixtures.N;Case 'HU083-no-vencida' $n.id $n.dni habilitado habilitado GET "/api/pagos/jugador/$($n.id)/estado-de-cuenta" $null 200 unchanged
 Case 'beca-preventiva-suplemento' $n.id $n.dni habilitado habilitado POST "/api/players/$($n.id)/discount" @{reason='Becado';valueType='%';percentage=100;startDate=(Date $unexpired);endDate=(Date $current.AddMonths(2))} 201 benefit
 $f=$fixtures.F;$ids=Pending $f.id;Case 'rechazo-lote-prevalidacion' $f.id $f.dni inhabilitado inhabilitado POST '/api/pagos/cobro' @{idJugador=$f.id;idsPago=@($ids[0],999999999);metodoPago='Efectivo'} 400 unchanged
 [void](SQL "CREATE OR ALTER TRIGGER dbo.HU064_QA_Rollback ON dbo.PAGOS AFTER UPDATE AS BEGIN SET NOCOUNT ON; IF EXISTS(SELECT 1 FROM inserted i JOIN deleted d ON d.PK_id_pago=i.PK_id_pago WHERE i.FK_id_jugador=$($f.id) AND d.estado=0 AND i.estado=1) THROW 51064, 'HU064 controlled transaction rollback', 1; END")
 try { Case 'rollback-escritura-trigger' $f.id $f.dni inhabilitado inhabilitado POST '/api/pagos/cobro' @{idJugador=$f.id;idsPago=@($ids);metodoPago='Efectivo'} 500 unchanged } finally {[void](SQL "DROP TRIGGER IF EXISTS dbo.HU064_QA_Rollback; SELECT 1")}
 $paid=@((Snap $a.id).payments|Where-Object estado -eq 1)[0].PK_id_pago;Case 'anulacion' $a.id $a.dni solo_entrenamientos inhabilitado POST "/api/pagos/$paid/anular" @{motivo='HU064 revalidacion API real'} 200 annul
 Case '0-a-1-generacion' $e.id $e.dni habilitado solo_entrenamientos POST "/api/cuotas-faltantes/$($p1.Year)/$($p1.Month)/generar" $null 200 generation
 Case '1-a-2-generacion' $e.id $e.dni solo_entrenamientos inhabilitado POST "/api/cuotas-faltantes/$($p2.Year)/$($p2.Month)/generar" $null 200 generation
 $state=State $a.id $a.dni;$financial='(?i)"(monto|saldo|deuda|totalDebtAmount|metodoPago|paymentMethod|cuotas|fees|amountDue|monto_final)"'
 $privacy=@{case='privacidad';result='PASS';individualFinancialFields=[bool]($state.f.b-match$financial);categoryFinancialFields=[bool]($state.l.b-match$financial);sourceFields=@(($state.s.b|ConvertFrom-Json).PSObject.Properties.Name)}
 if($privacy.individualFinancialFields-or$privacy.categoryFinancialFields-or((@($privacy.sourceFields|Sort-Object)-join ',')-ne'accesoPermitido,dni,estado,fechaBaja,idJugador')){$privacy.result='FAIL'};$results.Add($privacy);Save
}catch{Save;@{error=$_.Exception.Message;at=Now;stack=$_.ScriptStackTrace}|ConvertTo-Json|Set-Content (Join-Path $PSScriptRoot "error-$Run.json");throw}










