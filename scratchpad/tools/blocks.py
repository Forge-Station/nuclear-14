import io, re, subprocess, sys, os

AUDIT = "C:/Users/Semen/AppData/Local/Temp/claude/D--GitHub-nuclear-14/21a4067a-12a4-4607-b243-caba63b22cf9/scratchpad/upstream-audit"
THEIRS = "b20a6845847b9f6025d5170c044186cbe0836e7b"
MARK = re.compile(r'_?Shitmed|Forge|Corvax|_?Nuclear14|\bN14\b|DeltaV|_DV\b|Goob|Frontier|_NF\b|EinsteinEngines|\bTTS\b|Imp-Change', re.I)
START = re.compile(r'(Start|Begin)\s*$|-Start', re.I)
END = re.compile(r'(End)\s*$|-End', re.I)

def read(p):
    return io.open(p, encoding='utf-8-sig', errors='replace').read().replace('\r\n', '\n').split('\n')

def upstream(f):
    r = subprocess.run(['git', 'show', f'{THEIRS}:{f}'], capture_output=True)
    if r.returncode: return None
    return r.stdout.decode('utf-8-sig', 'replace').replace('\r\n', '\n').split('\n')

def regions(lines):
    """Найти помеченные области: парные Start/End или одиночные строки."""
    hits = [i for i, l in enumerate(lines) if MARK.search(l)]
    out, used = [], set()
    i = 0
    while i < len(hits):
        a = hits[i]
        if a in used:
            i += 1; continue
        if START.search(lines[a]):
            # искать парный End
            for b in hits[i+1:]:
                if END.search(lines[b]):
                    out.append((a, b)); used.update(range(a, b+1)); break
            else:
                out.append((a, a)); used.add(a)
        else:
            out.append((a, a)); used.add(a)
        while i < len(hits) and hits[i] in used: i += 1
    return out

def main(f, ctx=4):
    fk = read(os.path.join(AUDIT, 'fork-side-224', f))
    up = upstream(f)
    ups = '\n'.join(up) if up else ''
    print(f'######## {f}   (форк {len(fk)} стр, апстрим {len(up) if up else "НЕТ"} стр)')
    for (a, b) in regions(fk):
        lo, hi = max(0, a-ctx), min(len(fk), b+ctx+1)
        body = '\n'.join(fk[a:b+1])
        present = body.strip() and body.strip() in ups
        anchor_before = fk[lo:a]
        anch_ok = all((l.strip() in ups) for l in anchor_before if l.strip())
        print(f'--- блок {a+1}-{b+1}  уже_в_апстриме={present}  якорь_выше_найден={anch_ok}')
        for i in range(lo, hi):
            tag = '>>' if a <= i <= b else '  '
            print(f'{tag}{i+1:5d}| {fk[i]}')
        print()

if __name__ == '__main__':
    for f in sys.argv[1:]:
        main(f)
