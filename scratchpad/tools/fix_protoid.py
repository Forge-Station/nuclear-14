"""Убирает удалённые движком сериализаторы PrototypeId*Serializer<> и атрибуты
[ValidatePrototypeId<>]. Поля остаются строковыми — поведение не меняется,
теряется только валидация id при загрузке прототипов."""
import io
import pathlib
import re

BACKSLASH = chr(92)
ROOTS = ['Content.Shared', 'Content.Server', 'Content.Client', 'Content.IntegrationTests']

SER = re.compile(
    r'(,\s*)?customTypeSerializer\s*:\s*typeof\(\s*'
    r'PrototypeId(?:List|HashSet|Dictionary)?Serializer\s*<[^>]*>\s*\)(\s*,)?')
ATTR_LINE = re.compile(r'^\s*\[\s*ValidatePrototypeId\s*<[^>]*>\s*\]\s*$')
ATTR_INLINE = re.compile(r'\[\s*ValidatePrototypeId\s*<[^>]*>\s*\]\s*')

changed = []
n_ser = n_attr = 0

for root in ROOTS:
    for path in pathlib.Path(root).rglob('*.cs'):
        try:
            raw = io.open(path, encoding='utf-8-sig').read()
        except OSError:
            continue
        if 'PrototypeId' not in raw and 'ValidatePrototypeId' not in raw:
            continue

        newline = '\r\n' if '\r\n' in raw else '\n'
        text = raw.replace('\r\n', '\n')
        before = text

        text, count = SER.subn('', text)
        n_ser += count

        # подчистить следы удалённого аргумента
        text = re.sub(r'\(\s*,\s*', '(', text)
        text = re.sub(r',\s*\)', ')', text)
        text = re.sub(r'\[DataField\(\s*\)\]', '[DataField]', text)

        kept = []
        for line in text.split('\n'):
            if ATTR_LINE.match(line):
                n_attr += 1
                continue
            if 'ValidatePrototypeId' in line:
                stripped = ATTR_INLINE.sub('', line)
                if stripped != line:
                    n_attr += 1
                    line = stripped
                    if not line.strip():
                        continue
            kept.append(line)
        text = '\n'.join(kept)

        if text != before:
            io.open(path, 'w', encoding='utf-8', newline=newline).write(text)
            changed.append(str(path).replace(BACKSLASH, '/'))

print(f'файлов изменено: {len(changed)}; сериализаторов убрано: {n_ser}; атрибутов убрано: {n_attr}')
for name in changed:
    print('  ', name)
