# Intentos descartados del harness

Estos errores pertenecen a intentos previos a las dos corridas finales y NO describen fallas funcionales del producto. Las marcas UTC originales se conservan dentro de cada JSON.

Se corrigieron colección JSON vacía/un solo elemento en Windows PowerShell 5, colisión de parámetros A/a y B/b, codificación UTF8 sin BOM del literal AL DÍA, HTTP real422 de beca (antes esperado400), y bloqueo transitorio al escribir evidencia (ahora publicación atómica). Se detuvo ante errores de fixtures; luego se repitieron suites completas con jugadores nuevos. Los JSON parciales anteriores se reemplazaron durante la ejecución incremental y no se reconstruyen como evidencia.

Resultados finales: ../results-principal.json y ../results-regresion.json. Las operaciones bajo prueba siempre fueron API reales. SQL se limitó a fixtures, lecturas y trigger temporal controlado para fallo de UPDATE.
