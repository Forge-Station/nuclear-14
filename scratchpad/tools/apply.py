"""Применяет к файлам правки вида (файл, старое, новое).
Сначала git checkout --theirs, затем точные замены. Падает громко."""
import io, subprocess, sys, json

def resolve(edits, add=True):
    files = []
    for f, pairs in edits:
        r = subprocess.run(['git','checkout','--theirs','--',f], capture_output=True)
        if r.returncode:
            print(f'!! checkout failed: {f}: {r.stderr.decode(errors="replace")[:200]}'); continue
        s = io.open(f, encoding='utf-8-sig').read().replace('\r\n','\n')
        ok = True
        for old, new in pairs:
            old = old.replace('\r\n','\n'); new = new.replace('\r\n','\n')
            n = s.count(old)
            if n != 1:
                print(f'!! {f}: якорь встречается {n} раз:\n---\n{old[:300]}\n---')
                ok = False; break
            s = s.replace(old, new, 1)
        if not ok:
            continue
        io.open(f,'w',encoding='utf-8',newline='\n').write(s)
        files.append(f)
        print('ok:', f)
    if add and files:
        subprocess.run(['git','add','--'] + files, check=True)
    return files
