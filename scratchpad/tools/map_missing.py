"""Для каждого отсутствующего типа из лога сборки находит файл, где он был объявлен
в форке до мерджа, и говорит, был ли этот файл форк-овым (можно вернуть целиком)
или апстримным (апстрим его переписал — тип надо спасать отдельно)."""
import re
import subprocess
import sys

FORK = 'c48191ec4c'
UPSTREAM = 'upstream/master'
ERR_LOG = sys.argv[1] if len(sys.argv) > 1 else '/tmp/e7.txt'

MISSING = re.compile(r'(?:имени пространства имен|имен) "([A-Za-z_][A-Za-z0-9_]*)(?:<>)?"')
SKIP = {'Body', 'Part', 'Organ', 'List', 'Set', 'Popups', 'Content'}

names = set()
for line in open(ERR_LOG, encoding='utf-8', errors='replace'):
    for m in MISSING.finditer(line):
        n = m.group(1)
        if n not in SKIP:
            names.add(n)


def grep_tree(ref, typename):
    pat = r'(class|interface|struct|record|enum)[A-Za-z ]* ' + typename + r'\b'
    r = subprocess.run(['git', 'grep', '-l', '-E', pat, ref, '--',
                        'Content.Shared', 'Content.Server', 'Content.Client'],
                       capture_output=True)
    out = r.stdout.decode('utf-8', 'replace').strip()
    if not out:
        return []
    return [l.split(':', 1)[1] for l in out.split('\n') if ':' in l]


rows = []
for n in sorted(names):
    fork_files = grep_tree(FORK, n)
    up_files = grep_tree(UPSTREAM, n)
    if not fork_files:
        rows.append((n, 'НЕТ И В ФОРКЕ', ''))
        continue
    f = fork_files[0]
    if up_files:
        rows.append((n, 'ЕСТЬ В АПСТРИМЕ -> переименование/перенос', up_files[0]))
    else:
        # файл существовал у апстрима? если да - он его переписал
        r = subprocess.run(['git', 'cat-file', '-e', f'{UPSTREAM}:{f}'], capture_output=True)
        kind = 'апстрим переписал файл -> спасти тип' if r.returncode == 0 else 'форк-овый файл -> вернуть целиком'
        rows.append((n, kind, f))

width = max(len(r[0]) for r in rows) if rows else 10
for n, kind, f in rows:
    print(f'{n:<{width}}  {kind:<42}  {f}')
