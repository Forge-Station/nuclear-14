"""Добавляет нужные using'и в файлы, где тип переехал в другое пространство имён,
и убирает осиротевшие using'и удалённых движком сериализаторов."""
import io
import pathlib
import re
import sys

BACKSLASH = chr(92)
ROOTS = ['Content.Shared', 'Content.Server', 'Content.Client', 'Content.IntegrationTests']

# тип -> пространство имён, которое надо подключить
MOVED = {
    'DamageableComponent': 'Content.Shared.Damage.Components',
    'DamageableSystem': 'Content.Shared.Damage.Systems',
    'DamageModifyEvent': 'Content.Shared.Damage.Systems',
    'DamageChangedEvent': 'Content.Shared.Damage.Systems',
    'UseDelaySystem': 'Content.Shared.Timing.Systems',
}

# using'и удалённых пространств имён движка
DEAD_USING = re.compile(
    r'^\s*using\s+Robust\.Shared\.Serialization\.TypeSerializers\.Implementations\.Custom\.Prototype[^;]*;\s*$')

added = {}
dropped = []

for root in ROOTS:
    for path in pathlib.Path(root).rglob('*.cs'):
        try:
            raw = io.open(path, encoding='utf-8-sig').read()
        except OSError:
            continue

        newline = '\r\n' if '\r\n' in raw else '\n'
        text = raw.replace('\r\n', '\n')
        before = text
        lines = text.split('\n')

        # 1. выкинуть мёртвые using'и
        kept = [l for l in lines if not DEAD_USING.match(l)]
        if len(kept) != len(lines):
            dropped.append(str(path).replace(BACKSLASH, '/'))
            lines = kept

        # 2. добавить using'и для переехавших типов
        body = '\n'.join(lines)
        needed = set()
        for typename, ns in MOVED.items():
            if re.search(r'\b' + typename + r'\b', body) and f'using {ns};' not in body:
                needed.add(ns)

        if needed:
            # вставить после последнего using в начале файла
            last_using = -1
            for i, l in enumerate(lines[:80]):
                if l.startswith('using '):
                    last_using = i
            insert_at = last_using + 1 if last_using >= 0 else 0
            for ns in sorted(needed, reverse=True):
                lines.insert(insert_at, f'using {ns};')
            added.setdefault(str(path).replace(BACKSLASH, '/'), []).extend(sorted(needed))

        text = '\n'.join(lines)
        if text != before:
            io.open(path, 'w', encoding='utf-8', newline=newline).write(text)

print(f'мёртвых using убрано в файлах: {len(dropped)}')
for name in dropped:
    print('  -', name)
print(f'using добавлен в файлах: {len(added)}')
for name, nss in sorted(added.items()):
    print('  +', name, '->', ', '.join(nss))
