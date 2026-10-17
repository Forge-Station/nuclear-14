"""Ищет прототипы, которые ссылаются на несуществующего родителя.

Мердж брал Resources со стороны апстрима, поэтому форковые сущности внутри
апстримных файлов исчезли, а ссылки на них остались - на старте это падение.
Разбор строчный: в прототипах есть кастомные теги вроде !type:, на которых
обычный YAML-парсер спотыкается.
"""
import io
import pathlib
import re
import subprocess
import sys
from collections import defaultdict

ROOT = pathlib.Path('Resources/Prototypes')
FORK = 'c48191ec4c'

ENTRY = re.compile(r'^- type:\s*(\S+)')
ID = re.compile(r'^  id:\s*(\S+)')
PARENT_INLINE = re.compile(r'^  parent:\s*(\S.*)$')
PARENT_ITEM = re.compile(r'^  -\s*(\S+)\s*(?:#.*)?$')


def collect(paths_text):
    """{(kind, id): [parents]} по тексту каждого файла."""
    decls = {}
    for rel, raw in paths_text:
        lines = raw.replace('\r\n', '\n').split('\n')
        starts = [i for i, line in enumerate(lines) if ENTRY.match(line)]
        for n, start in enumerate(starts):
            end = starts[n + 1] if n + 1 < len(starts) else len(lines)
            block = lines[start:end]
            kind = ENTRY.match(block[0]).group(1)
            name = next((ID.match(b).group(1) for b in block if ID.match(b)), None)
            if name is None:
                continue

            parents = []
            for i, b in enumerate(block):
                m = PARENT_INLINE.match(b)
                if not m:
                    continue
                value = m.group(1).split('#')[0].strip()
                if value.startswith('['):
                    parents += [p.strip() for p in value.strip('[]').split(',') if p.strip()]
                elif value:
                    parents.append(value)
                else:
                    for nxt in block[i + 1:]:
                        if not nxt.strip():
                            continue
                        item = PARENT_ITEM.match(nxt)
                        if not item:
                            break
                        parents.append(item.group(1))
            decls[(kind, name)] = (rel, parents)
    return decls


now_files = [(p.as_posix(), io.open(p, encoding='utf-8-sig', errors='replace').read())
             for p in sorted(ROOT.rglob('*.yml'))]
now = collect(now_files)
declared = defaultdict(set)
for (kind, name) in now:
    declared[kind].add(name)

dangling = []
for (kind, name), (rel, parents) in sorted(now.items()):
    for parent in parents:
        if parent not in declared[kind]:
            dangling.append((kind, name, parent, rel))

print(f'прототипов всего: {len(now)}')
print(f'ссылок на несуществующего родителя: {len(dangling)}')

if not dangling:
    sys.exit(0)

# Какие из потерянных родителей были в форке до мерджа - значит это потеря мерджа.
listing = subprocess.run(['git', 'ls-tree', '-r', '--name-only', FORK, 'Resources/Prototypes/'],
                         capture_output=True, check=True).stdout.decode().split('\n')
fork_files = []
for rel in listing:
    if not rel.endswith('.yml'):
        continue
    blob = subprocess.run(['git', 'show', f'{FORK}:{rel}'], capture_output=True)
    if blob.returncode == 0:
        fork_files.append((rel, blob.stdout.decode('utf-8-sig', errors='replace')))
fork = collect(fork_files)
fork_ids = defaultdict(set)
for (kind, name), (rel, _) in fork.items():
    fork_ids[kind].add(name)

by_parent = defaultdict(list)
for kind, name, parent, rel in dangling:
    by_parent[(kind, parent)].append((name, rel))

print()
print('# родитель | вид | было в форке | где определялось в форке | сколько детей')
for (kind, parent), kids in sorted(by_parent.items(), key=lambda kv: -len(kv[1])):
    was = parent in fork_ids[kind]
    where = fork.get((kind, parent), ('-', None))[0] if was else '-'
    print(f'{parent} | {kind} | {"да" if was else "нет"} | {where} | {len(kids)}')
