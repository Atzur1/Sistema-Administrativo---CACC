#!/usr/bin/env bash
# Uso: log2annot.sh <titulo> <archivo-log>. Publica las líneas de error del log como anotaciones.
grep -E "✗|×|FAIL|Error:|ERROR|error TS|NG[0-9]{4}|AssertionError|Expected" "$2" | grep -v "^\s*$" | head -9 | while IFS= read -r l; do
  echo "::error title=$1::$(echo "$l" | cut -c1-600)"
done
tail -5 "$2" | while IFS= read -r l; do echo "::notice title=$1 (final)::$(echo "$l" | cut -c1-400)"; done
