"""Печатает все блоки '- type: NightVision' из прототипов с их полями."""
import io
import pathlib
import re

ROOT = pathlib.Path('Resources/Prototypes')
START = re.compile(r'^(\s*)-\s*type:\s*NightVision\s*(#.*)?$')

for path in sorted(ROOT.rglob('*.yml')):
    lines = io.open(path, encoding='utf-8-sig').read().replace('\r\n', '\n').split('\n')
    for i, line in enumerate(lines):
        m = START.match(line)
        if not m:
            continue
        indent = len(m.group(1))
        fields = []
        for nxt in lines[i + 1:]:
            if not nxt.strip():
                continue
            cur = len(nxt) - len(nxt.lstrip())
            if cur <= indent:
                break
            fields.append(nxt.strip())
        print(f'{path.as_posix()}:{i + 1}  ' + ('; '.join(fields) if fields else '(без полей)'))
