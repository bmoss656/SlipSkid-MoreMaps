"""Road kit connector analysis over the kit-<scene>.json dumps.

For every flat-laid tile (rotation x = 270), finds the other tiles whose footprint touches one of its four
side faces and records which face and which stretch of it, in the tile's own frame (metres). The most common
entries per piece type are its connectors. Also prints, for slopes, the height of the flat road met at each end.

Usage: python tools/kit_analysis.py "<BepInEx>/plugins/MoreMaps" [R018B101 ...]
"""
import collections
import glob
import json
import math
import os
import sys


def rot(e):
    """Unity Quaternion.Euler(x, y, z) as a 3x3 matrix (Ry * Rx * Rz)."""
    x, y, z = (math.radians(a) for a in e)
    rx = [[1, 0, 0], [0, math.cos(x), -math.sin(x)], [0, math.sin(x), math.cos(x)]]
    ry = [[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]]
    rz = [[math.cos(z), -math.sin(z), 0], [math.sin(z), math.cos(z), 0], [0, 0, 1]]
    mm = lambda a, b: [[sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3)] for i in range(3)]
    return mm(ry, mm(rx, rz))


def apply(m, v):
    return [sum(m[i][k] * v[k] for k in range(3)) for i in range(3)]


def transpose(m):
    return [[m[j][i] for j in range(3)] for i in range(3)]


def to_world(t, local):
    w = apply(rot(t['pose']['rot']), local)
    return [w[i] + t['pose']['pos'][i] for i in range(3)]


def to_local(t, p):
    return apply(transpose(rot(t['pose']['rot'])), [p[i] - t['pose']['pos'][i] for i in range(3)])


def footprint(t, dump):
    """The tile mesh's bounds in the tile frame, in metres."""
    m = dump['meshes'].get(t['nonBatchedMesh'])
    if not m:
        return None
    c, s, k = m['bounds']['center'], m['bounds']['size'], t['pose']['scale'][0]
    return [(c[i] - s[i] / 2) * k for i in range(3)], [(c[i] + s[i] / 2) * k for i in range(3)]


def main():
    folder = sys.argv[1]
    only = set(sys.argv[2:])
    stats = collections.defaultdict(collections.Counter)
    sizes = {}
    for f in sorted(glob.glob(os.path.join(folder, 'kit-*.json'))):
        dump = json.load(open(f))
        roots = [t for t in dump['tiles'] if '/' not in t['path'] and t['code']
                 and t['pose']['rot'][0] == 270.0 and '_MoreMaps' not in t['name']]
        boxes = {id(t): footprint(t, dump) for t in roots}
        for a in roots:
            if not boxes[id(a)]:
                continue
            lo, hi = boxes[id(a)]
            sizes.setdefault(a['code'], ([round(v) for v in lo], [round(v) for v in hi]))
            for b in roots:
                if b is a or not boxes[id(b)]:
                    continue
                if sum((a['pose']['pos'][i] - b['pose']['pos'][i]) ** 2 for i in range(3)) > 60 ** 2:
                    continue
                blo, bhi = boxes[id(b)]
                corners = [to_local(a, to_world(b, [x, y, z]))
                           for x in (blo[0], bhi[0]) for y in (blo[1], bhi[1]) for z in (blo[2], bhi[2])]
                mn = [min(c[i] for c in corners) for i in range(3)]
                mx = [max(c[i] for c in corners) for i in range(3)]
                if mx[2] < lo[2] - 1 or mn[2] > hi[2] + 1:
                    continue

                def overlap(i):
                    o0, o1 = max(mn[i], lo[i]), min(mx[i], hi[i])
                    return (round(o0), round(o1)) if o1 - o0 > 1 else None

                for face, touching, axis in (('-X', abs(mx[0] - lo[0]) < 0.6, 1), ('+X', abs(mn[0] - hi[0]) < 0.6, 1),
                                             ('-Y', abs(mx[1] - lo[1]) < 0.6, 0), ('+Y', abs(mn[1] - hi[1]) < 0.6, 0)):
                    if touching and overlap(axis):
                        stats[a['code']][(face, overlap(axis))] += 1
    for code in sorted(stats):
        if only and code not in only:
            continue
        print(code, 'footprint', sizes.get(code), stats[code].most_common(6))


if __name__ == '__main__':
    main()
