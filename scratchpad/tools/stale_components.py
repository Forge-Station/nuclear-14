"""Находит компоненты, объявленные дважды под одним именем класса там, где это
фатально (один процесс), и у которых ровно один из путей есть у апстрима.

Такой путь апстрим перенёс, а мердж оставил и старую копию. Печатает список
устаревших форк-путей и namespace, на который надо перенаправить потребителей.
"""
import io
import pathlib
import re
import subprocess
from collections import defaultdict

BACKSLASH = chr(92)
UPSTREAM = 'upstream/master'
ROOTS = ['Content.Shared', 'Content.Server', 'Content.Client']

REGISTER = re.compile(r'\[\s*(?:[\w\s,()<>."]*\b)?RegisterComponent\b')
DECL = re.compile(r'\bclass\s+(\w*Component)\b')
NS = re.compile(r'^\s*namespace\s+([\w.]+)', re.M)

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
        for name in DECL.findall(text):
            by_class[name].append((rel, ns))


def project(rel):
    return rel.split('/')[0]


def in_upstream(rel):
    return subprocess.run(['git', 'cat-file', '-e', f'{UPSTREAM}:{rel}'],
                          capture_output=True).returncode == 0


def same_process(projects):
    # серверный процесс: Shared+Server, клиентский: Shared+Client
    return ('Content.Shared' in projects and len(projects) > 1) or len(projects) == 1


rows = []
for name, entries in sorted(by_class.items()):
    files = sorted({rel for rel, _ in entries})
    if len(files) < 2:
        continue
    if not same_process({project(f) for f in files}):
        continue

    ups = [f for f in files if in_upstream(f)]
    forks = [f for f in files if f not in ups]
    if len(ups) != 1 or not forks:
        continue

    keep_ns = next(ns for rel, ns in entries if rel == ups[0])
    for stale in forks:
        stale_ns = next(ns for rel, ns in entries if rel == stale)
        rows.append((name, stale, stale_ns, ups[0], keep_ns))

print(f'# устаревших форк-копий компонентов: {len(rows)}')
print('# формат: КЛАСС | УДАЛИТЬ | его namespace | ОСТАВИТЬ | его namespace')
for r in rows:
    print(' | '.join(r))
