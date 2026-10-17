"""Переводит форковые блоки '- type: NightVision' на поля апстримного компонента.

Форк держал ночное зрение на своём фреймворке Switchable, апстрим - на
собственном NightVisionComponent. Имя компонента в прототипах одно, поэтому
поля надо переименовать: иначе прототипы просто не загрузятся.
"""
import io
import pathlib
import re

ROOT = pathlib.Path('Resources/Prototypes')
START = re.compile(r'^(\s*)-\s*type:\s*NightVision\s*(#.*)?$')
FIELD = re.compile(r'^(\s*)([A-Za-z]\w*):\s*(.*)$')

RENAME = {
    'isEquipment': 'relayOverlay',
    'isActive': 'enabled',
    'toggleAction': 'action',
    'color': 'overlayColor',
}
# Звуки включения/выключения жили на Switchable; у апстрима их нет вовсе,
# а во всех форковых прототипах они и так выставлены в null.
DROP = {'activateSound', 'deactivateSound'}
FORK_ONLY = set(RENAME) | DROP

changed = []
for path in sorted(ROOT.rglob('*.yml')):
    raw = io.open(path, encoding='utf-8-sig').read()
    newline = '\r\n' if '\r\n' in raw else '\n'
    bom = io.open(path, 'rb').read(3) == b'\xef\xbb\xbf'
    lines = raw.replace('\r\n', '\n').split('\n')

    out, i, hits = [], 0, 0
    while i < len(lines):
        m = START.match(lines[i])
        if not m:
            out.append(lines[i])
            i += 1
            continue

        indent = len(m.group(1))
        j = i + 1
        block = []
        while j < len(lines):
            line = lines[j]
            if line.strip() and len(line) - len(line.lstrip()) <= indent:
                break
            block.append(line)
            j += 1

        names = {fm.group(2) for fm in (FIELD.match(b) for b in block) if fm}
        if not names & FORK_ONLY:
            out.extend([lines[i]] + block)
            i = j
            continue

        hits += 1
        field_indent = next(
            (fm.group(1) for fm in (FIELD.match(b) for b in block) if fm),
            ' ' * (indent + 2))

        kept = []
        for line in block:
            fm = FIELD.match(line)
            if not fm:
                kept.append(line)
                continue
            name = fm.group(2)
            if name in DROP:
                continue
            kept.append(f'{fm.group(1)}{RENAME.get(name, name)}: {fm.group(3)}'.rstrip())

        present = {fm.group(2) for fm in (FIELD.match(k) for k in kept) if fm}
        # Форковый IsActive по умолчанию был false, а действие переключения -
        # "ToggleNightVision"; у апстрима Enabled по умолчанию true и действия нет.
        if 'enabled' not in present:
            kept.append(f'{field_indent}enabled: false')
        if 'action' not in present:
            kept.append(f'{field_indent}action: ActionToggleNightVision')

        out.append(lines[i])
        out.extend(kept)
        i = j

    if not hits:
        continue
    text = newline.join(out)
    io.open(path, 'w', encoding='utf-8-sig' if bom else 'utf-8', newline='').write(text)
    changed.append((path.as_posix(), hits))

for name, hits in changed:
    print(f'{hits}  {name}')
print(f'всего блоков: {sum(h for _, h in changed)}')
