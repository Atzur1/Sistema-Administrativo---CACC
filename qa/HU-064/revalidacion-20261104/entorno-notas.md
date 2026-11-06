# Entorno aislado de HU-064

Se restauró un backup COPY_ONLY de ClubCamionerosUnificadaV2_Prueba en HU064_Revalidacion_20261104 (LocalDB del usuario FramD). El backup temporal se eliminó después de restaurar. Los archivos de datos permanecen ignorados por Git.

Las APIs existentes en 5318/5319 (PID 18988/15960) no se detuvieron ni reconfiguraron. Las APIs aisladas usan 5328/5329 (PID inicial 31312/17508), las DLL que ya existían y variables de entorno que sustituyen la conexión y URL entre sistemas. No se compiló, ejecutó armar.ps1 ni se modificó código de producto. La clave de sistema compartida se leyó únicamente en memoria de configuración local y se transmitió mediante variables de entorno; no se guarda en estos artefactos.

Los primeros intentos de inicio 27292/7300 abortaron porque los puertos originales ya estaban ocupados: los logs administrativo/deportivo sin prefijo aislado documentan ese intento y NO son evidencia de los servicios utilizados en las pruebas.

Las ramas actuales no identifican automáticamente la fuente exacta de binarios anteriores. repositorios.json registra ramas y commits observados; binarios.json registra el SHA256 y LastWriteTimeUtc de cada DLL de producto usada. Los C# de producción administrativos en workspace y snapshot coinciden al normalizar finales de línea. El snapshot deportivo conserva el workaround anterior: difiere del fix actual en Program.cs/EstadoDeudaClient.cs y no contiene ClienteAdministrativo.cs. No se debe presentar la ejecución como validación del commit deportivo a9e690f5 exacto.

La sesión XE temporal HU064_Revalidacion_20261104 captura sql_transaction únicamente para database_id 27; transaction_state=1 representa COMMIT. El instante de registro de PAGOS no equivale al COMMIT. Solo es válido asignar commitAt si se correlaciona el evento mediante transacción/sesión e intervalo de operación y no existe ambigüedad. La sesión se elimina al terminar ambas corridas, conservando evidencia exportada sin secretos.
