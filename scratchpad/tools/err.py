"""Печатает ошибки сборки компактно: путь от корня репозитория, строка,
код и текст. Фильтр по подстроке пути - первым аргументом после лога."""
import io
import re
import sys

sys.stdout.reconfigure(encoding='utf-8', errors='backslashreplace')

LOG = sys.argv[1]
NEEDLE = sys.argv[2] if len(sys.argv) > 2 else ''
MARK = 'nuclear-14'
SUFFIX = re.compile(r'\s*\[[A-Za-z]:[^\]]*\]\s*$')

seen = set()
for line in io.open(LOG, encoding='utf-8', errors='backslashreplace'):
    if ': error ' not in line:
        continue
    row = SUFFIX.sub('', line.strip())
    head, sep, tail = row.partition(MARK)
    if sep:
        row = tail.lstrip('\/')
    if NEEDLE and NEEDLE not in row:
        continue
    if row in seen:
        continue
    seen.add(row)
    print(row)
