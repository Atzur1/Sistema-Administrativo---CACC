# Convierte los .trx de dotnet test en anotaciones de GitHub (::error) y un resumen (::notice).
import glob, sys, xml.etree.ElementTree as ET
ns = {'t': 'http://microsoft.com/schemas/VisualStudio/TeamTest/2010'}
tot = {'total': 0, 'passed': 0, 'failed': 0, 'notExecuted': 0}
fallas = []
for f in glob.glob(sys.argv[1] + '/**/*.trx', recursive=True):
    r = ET.parse(f).getroot()
    c = r.find('.//t:ResultSummary/t:Counters', ns)
    for k in tot: tot[k] += int(c.get(k, 0))
    for u in r.findall('.//t:UnitTestResult', ns):
        if u.get('outcome') == 'Failed':
            msg = u.find('.//t:Message', ns)
            texto = (msg.text or '') if msg is not None else ''
            fallas.append((u.get('testName'), texto.strip().replace('\n', ' ')[:600]))
print(f"::notice title={sys.argv[2]}::total={tot['total']} ok={tot['passed']} fallidas={tot['failed']} salteadas={tot['notExecuted']}")
for n, m in fallas[:9]:
    print(f"::error title=FALLA {n[:120]}::{m}")
if len(fallas) > 9: print(f"::error title=Más fallas::{len(fallas) - 9} pruebas fallidas más")
