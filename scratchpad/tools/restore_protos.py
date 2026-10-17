"""Переносит прототипы из версии файла до мерджа в текущую: мердж брал
Resources со стороны апстрима и вместе с этим терял форковые сущности.

Аргументы: путь к файлу, затем id прототипов, которые надо вернуть.
"""
import io
import re
import subprocess
import sys

FORK = 'c48191ec4c'
PATH = sys.argv[1]
WANTED = sys.argv[2:]

old = subprocess.run(['git', 'show', f'{FORK}:{PATH}'],
                     capture_output=True, check=True).stdout.decode('utf-8-sig')
lines = old.replace('\r\n', '\n').split('\n')

# Режем файл на блоки прототипов.
starts = [i for i, line in enumerate(lines) if line.startswith('- type: ')]
blocks = {}
for n, start in enumerate(starts):
    end = starts[n + 1] if n + 1 < len(starts) else len(lines)
    block = lines[start:end]
    match = next((re.match(r'^  id:\s*(\S+)', b) for b in block if re.match(r'^  id:\s*\S+', b)), None)
    if match:
        blocks[match.group(1)] = '\n'.join(block).rstrip('\n')

missing = [w for w in WANTED if w not in blocks]
if missing:
    print('!! не нашлось в версии до мерджа:', ', '.join(missing))

text = io.open(PATH, encoding='utf-8-sig').read()
bom = io.open(PATH, 'rb').read(3) == b'\xef\xbb\xbf'
newline = '\r\n' if '\r\n' in text else '\n'
body = text.replace('\r\n', '\n').rstrip('\n')

added = []
for name in WANTED:
    if name not in blocks:
        continue
    if re.search(r'^  id:\s*' + re.escape(name) + r'\s*$', body, re.M):
        print('уже есть:', name)
        continue
    body += '\n\n' + blocks[name]
    added.append(name)

if added:
    body = body + '\n'
    io.open(PATH, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline=newline).write(body)
print(f'{PATH}: возвращено {len(added)} -> {", ".join(added) if added else "-"}')
