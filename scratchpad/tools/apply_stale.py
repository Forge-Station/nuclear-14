"""Удаляет устаревшие форк-копии компонентов и добавляет потребителям using
на namespace, куда апстрим перенёс компонент."""
import io
import pathlib
import re
import subprocess
import sys
from collections import defaultdict

BACKSLASH = chr(92)
LIST_PATH = sys.argv[1]
ROOTS = ['Content.Shared', 'Content.Server', 'Content.Client', 'Content.IntegrationTests']

rows = []
for line in io.open(LIST_PATH, encoding='utf-8'):
    if ' | ' not in line or line.startswith('#'):
        continue
    name, stale, stale_ns, keep, keep_ns = line.rstrip('\n').split(' | ')
    rows.append((name, stale, stale_ns, keep, keep_ns))

# NightVision: у форка своя реализация на фреймворке Switchable, но имя компонента
# в прототипах одно, различить нельзя. Оставляем апстримную (она тоже переключаемая),
# форк-овую удаляем вместе с её системой.
extra_deletes = ['Content.Shared/Overlays/Switchable/NightVisionSystem.cs']

deleted, ns_needed = [], defaultdict(set)
for name, stale, stale_ns, keep, keep_ns in rows:
    r = subprocess.run(['git', 'rm', '-q', '-f', '--', stale], capture_output=True)
    if r.returncode:
        print('!! не удалось удалить:', stale)
        continue
    deleted.append(stale)
    ns_needed[name].add(keep_ns)

for extra in extra_deletes:
    r = subprocess.run(['git', 'rm', '-q', '-f', '--', extra], capture_output=True)
    if r.returncode == 0:
        deleted.append(extra)

print(f'удалено файлов: {len(deleted)}')

# потребителям — using на новый namespace
patched = 0
for root in ROOTS:
    for path in pathlib.Path(root).rglob('*.cs'):
        rel = str(path).replace(BACKSLASH, '/')
        if '/obj/' in rel:
            continue
        try:
            text = io.open(path, encoding='utf-8-sig').read()
        except OSError:
            continue

        newline = '\r\n' if '\r\n' in text else '\n'
        body = text.replace('\r\n', '\n')
        want = set()
        for name, namespaces in ns_needed.items():
            if re.search(r'(?<![A-Za-z])' + name + r'(?![A-Za-z])', body):
                for ns in namespaces:
                    if f'using {ns};' not in body:
                        want.add(ns)
        if not want:
            continue

        lines = body.split('\n')
        idx = [i for i, l in enumerate(lines[:120]) if l.startswith('using ')]
        at = (idx[-1] + 1) if idx else 0
        for ns in sorted(want, reverse=True):
            lines.insert(at, f'using {ns};')
        io.open(path, 'w', encoding='utf-8', newline=newline).write('\n'.join(lines))
        patched += 1

print(f'потребителей с добавленным using: {patched}')
