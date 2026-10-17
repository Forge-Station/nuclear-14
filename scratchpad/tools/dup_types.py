"""Ищет типы, объявленные в двух и более файлах с одним и тем же namespace.
Именно так проявлялись переименования апстрима, где мердж оставил и старый,
и новый путь."""
import io
import pathlib
import re
import sys
from collections import defaultdict

BACKSLASH = chr(92)
ROOTS = sys.argv[1:] or ['Content.Shared', 'Content.Server', 'Content.Client']

NS = re.compile(r'^\s*namespace\s+([\w.]+)', re.M)
DECL = re.compile(
    r'^\s*(?:\[[^\]]*\]\s*)*'
    r'(?:public|internal|private|protected)?\s*'
    r'(?:static\s+|sealed\s+|abstract\s+|partial\s+|readonly\s+|ref\s+)*'
    r'(class|struct|record|interface|enum)\s+(\w+)', re.M)

# partial-типы законно живут в нескольких файлах
PARTIAL = re.compile(r'\bpartial\s+(?:class|struct|record|interface)\s+(\w+)')

seen = defaultdict(list)
partials = defaultdict(set)

for root in ROOTS:
    for path in pathlib.Path(root).rglob('*.cs'):
        if 'obj' + BACKSLASH in str(path) or '/obj/' in str(path).replace(BACKSLASH, '/'):
            continue
        try:
            text = io.open(path, encoding='utf-8-sig').read()
        except OSError:
            continue
        namespaces = NS.findall(text)
        ns = namespaces[0] if namespaces else '<global>'
        rel = str(path).replace(BACKSLASH, '/')

        for name in PARTIAL.findall(text):
            partials[(ns, name)].add(rel)

        for _, name in DECL.findall(text):
            seen[(ns, name)].append(rel)

dups = []
for (ns, name), files in seen.items():
    files = sorted(set(files))
    if len(files) < 2:
        continue
    # partial в нескольких файлах — норма
    if len(partials[(ns, name)]) >= len(files):
        continue
    dups.append((ns, name, files))

print(f'типов, объявленных в нескольких файлах: {len(dups)}')
for ns, name, files in sorted(dups):
    print(f'\n{ns}.{name}')
    for f in files:
        print('   ', f)
