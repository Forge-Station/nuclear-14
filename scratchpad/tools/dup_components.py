"""Ищет компоненты с одинаковым именем класса в разных namespace.

Robust берёт имя компонента из имени класса, поэтому такие пары:
  - ломают генератор состояний (RXN0003 duplicate hintName)
  - дают одинаковое имя компонента и падение при регистрации прототипов

Сборка только Content.Shared этого не покажет, если пара разнесена по проектам.
"""
import io
import pathlib
import re
from collections import defaultdict

BACKSLASH = chr(92)
ROOTS = ['Content.Shared', 'Content.Server', 'Content.Client']

REGISTER = re.compile(r'\[\s*(?:[\w\s,()<>."]*\b)?RegisterComponent\b')
DECL = re.compile(r'\bclass\s+(\w*Component)\b')
NS = re.compile(r'^\s*namespace\s+([\w.]+)', re.M)
PROTO_NAME = re.compile(r'ComponentProtoName\s*\(\s*"([^"]+)"')

by_class = defaultdict(list)

for root in ROOTS:
    for path in pathlib.Path(root).rglob('*.cs'):
        rel = str(path).replace(BACKSLASH, '/')
        if '/obj/' in rel:
            continue
        try:
            text = io.open(path, encoding='utf-8-sig').read()
        except OSError:
            continue
        if not REGISTER.search(text):
            continue

        ns_match = NS.search(text)
        ns = ns_match.group(1) if ns_match else '<global>'

        # имя компонента в прототипах: либо явное, либо имя класса без суффикса
        for name in DECL.findall(text):
            proto = PROTO_NAME.search(text)
            proto_name = proto.group(1) if proto else name[:-len('Component')]
            by_class[name].append((rel, ns, proto_name))

dups = {k: v for k, v in by_class.items() if len({f for f, _, _ in v}) > 1}

print(f'компонентов с одинаковым именем класса: {len(dups)}')
for name, entries in sorted(dups.items()):
    protos = {p for _, _, p in entries}
    flag = '  <-- и имя компонента совпадает' if len(protos) == 1 else ''
    print(f'\n{name}{flag}')
    for rel, ns, proto in sorted(entries):
        print(f'    {rel}   ns={ns}   type={proto}')
