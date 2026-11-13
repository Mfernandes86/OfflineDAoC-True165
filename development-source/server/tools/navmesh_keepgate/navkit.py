"""Read a Detour MSET/TESM .nav (OpenDAoC-BuildNav, 64-bit poly refs) into numpy arrays for gate-leak checks.

World (region) coordinates: X, Y, Z with Z up. Recast coordinates: (X/32, Z/32, Y/32).
Poly arrays are global over the file (tile order). Only ground polys (type 0) get edges; off-mesh link polys are
joined to the ground polys at their two ends.
"""
import struct
import numpy as np
from scipy.sparse import coo_matrix
from scipy.sparse.csgraph import connected_components, dijkstra

INV = 32.0
WALK, SWIM, DOOR, JUMP, DISABLED = 0x01, 0x02, 0x04, 0x08, 0x10
POLY_DT = np.dtype([("firstLink", "<u4"), ("verts", "<u2", 6), ("neis", "<u2", 6), ("flags", "<u2"),
                    ("vc", "u1"), ("at", "u1")])
HDR = "<4s14i3f7f"   # dtMeshHeader, 100 bytes


class Nav:
    def __init__(self, path):
        self.path = path
        self.buf = open(path, "rb").read()
        b = self.buf
        magic, ver, ntiles = struct.unpack_from("<4sii", b, 0)
        if magic != b"TESM" or ver != 1:
            raise ValueError("not a Detour set file: %s" % path)
        pos = 40
        flags, flag_off, ptype, vcount, cent, zmin, zmax, bbox, tile_of = [], [], [], [], [], [], [], [], []
        polyverts = []
        ia, ib, ext, offmesh = [], [], [], []
        self.tiles = []
        g = 0
        for _ in range(ntiles):
            ref, size = struct.unpack_from("<Qi", b, pos)
            pos += 16
            if not ref or not size:
                continue
            h = struct.unpack_from(HDR, b, pos)
            tx, ty, layer = h[2], h[3], h[4]
            npoly, nvert, nlink, ndet, ndv, ndt, nbv, noff = h[6:14]
            wc = h[17]
            p = pos + 100
            pos += size
            verts = np.frombuffer(b, "<f4", nvert * 3, p).reshape(-1, 3).astype(np.float64)
            p += nvert * 12
            poff = p
            polys = np.frombuffer(b, POLY_DT, npoly, p)
            p += npoly * 32 + nlink * 16 + ndet * 12 + ndv * 12 + ndt * 4 + nbv * 16
            offs = []
            for i in range(noff):
                pv = struct.unpack_from("<6f", b, p)
                rad, = struct.unpack_from("<f", b, p + 24)
                poly, cflags, side = struct.unpack_from("<HBB", b, p + 28)
                offs.append((pv[:3], pv[3:], rad, poly, cflags & 1))
                p += 36
            self.tiles.append(dict(tx=tx, ty=ty, layer=layer, base=g, npoly=npoly, wc=wc))
            world = verts[:, [0, 2, 1]] * INV
            for i in range(npoly):
                vc = int(polys["vc"][i])
                vv = world[polys["verts"][i][:vc]]
                polyverts.append(vv)
                flags.append(int(polys["flags"][i]))
                flag_off.append(poff + 32 * i + 28)
                ptype.append(int(polys["at"][i]) >> 6)
                vcount.append(vc)
                cent.append(vv.mean(axis=0))
                zmin.append(vv[:, 2].min()); zmax.append(vv[:, 2].max())
                bbox.append((vv[:, 0].min(), vv[:, 1].min(), vv[:, 0].max(), vv[:, 1].max()))
                tile_of.append(len(self.tiles) - 1)
            ground = (polys["at"] >> 6) == 0
            for j in range(6):
                nj = polys["neis"][:, j]
                valid = ground & (polys["vc"] > j)
                inn = valid & (nj > 0) & ((nj & 0x8000) == 0)
                src = np.nonzero(inn)[0]
                ia += list(src + g)
                ib += list(nj[inn].astype(np.int64) - 1 + g)
                ex = valid & ((nj & 0x8000) != 0)
                for k in np.nonzero(ex)[0]:
                    va = verts[polys["verts"][k, j]]
                    nx = j + 1 if polys["vc"][k] > j + 1 else 0
                    vb = verts[polys["verts"][k, nx]]
                    s = int(nj[k] & 0xff)
                    if s in (0, 4):
                        p, q = (va[2], va[1]), (vb[2], vb[1])
                    elif s in (2, 6):
                        p, q = (va[0], va[1]), (vb[0], vb[1])
                    else:
                        continue
                    p, q = sorted((p, q))
                    ext.append((tx, ty, s, int(k) + g, p, q, wc))
            for (s, e, rad, poly, bidir) in offs:
                offmesh.append((g + poly, np.array(s)[[0, 2, 1]] * INV, np.array(e)[[0, 2, 1]] * INV, rad * INV, bidir))
            g += npoly
        groups = {}
        loose = []   # border links only the older, looser rule makes (edge mid heights within 2 recast units)
        for e in ext:
            tx, ty, s = e[0], e[1], e[2]
            key = (tx, ty, 0) if s == 0 else (tx - 1, ty, 0) if s == 4 else (tx, ty, 2) if s == 2 else (tx, ty - 1, 2)
            groups.setdefault(key, ([], []))[0 if s in (0, 2) else 1].append(e)
        for A, B in groups.values():
            for a in A:
                for c in B:
                    if _overlap_slabs(a[4], a[5], c[4], c[5], 0.01, a[6]):
                        ia.append(a[3]); ib.append(c[3])
                    elif (min(a[5][0], c[5][0]) - max(a[4][0], c[4][0]) > 0.01 and
                          abs((a[4][1] + a[5][1]) - (c[4][1] + c[5][1])) / 2 < 2.0):
                        loose.append((a[3], c[3]))
        self.n = g
        self.flags = np.array(flags, np.int64)
        self.flag_off = np.array(flag_off, np.int64)
        self.ptype = np.array(ptype)
        self.cent = np.array(cent)
        self.zmin = np.array(zmin); self.zmax = np.array(zmax)
        self.bbox = np.array(bbox)
        self.polyverts = polyverts
        self.tile_of = np.array(tile_of)
        self.offmesh = offmesh
        self.ia = np.array(ia, np.int64)
        self.ib = np.array(ib, np.int64)
        # off-mesh links: join the link poly to the ground poly under each end (both ways; good enough for leaks)
        oa, ob = [], []
        for (lp, s, e, rad, bidir) in offmesh:
            for q in (s, e):
                n = self.locate(q, max(rad, 32), 64, ground_only=True)
                if n is not None:
                    oa.append(lp); ob.append(n)
        if oa:
            self.ia = np.concatenate([self.ia, oa]); self.ib = np.concatenate([self.ib, ob])
        # links = "detour": the links Detour makes at load (the truth); "union": those plus the looser rule's, for finding
        # leaks conservatively (a seal must hold on both)
        self.links = "detour"
        self.loose = np.array(loose, np.int64).reshape(-1, 2)

    # ---------------------------------------------------------------- geometry
    def contains(self, i, x, y):
        vv = self.polyverts[i]
        n = len(vv)
        sgn = 0
        for k in range(n):
            a, c = vv[k], vv[(k + 1) % n]
            cr = (c[0] - a[0]) * (y - a[1]) - (c[1] - a[1]) * (x - a[0])
            if abs(cr) < 1e-6:
                continue
            s = 1 if cr > 0 else -1
            if sgn == 0:
                sgn = s
            elif s != sgn:
                return False
        return True

    def height(self, i, x, y):
        """Z on the poly's plane fit (least squares over its verts) at x, y."""
        vv = self.polyverts[i]
        A = np.c_[vv[:, 0], vv[:, 1], np.ones(len(vv))]
        c, *_ = np.linalg.lstsq(A, vv[:, 2], rcond=None)
        return c[0] * x + c[1] * y + c[2]

    def near(self, x, y, r):
        bb = self.bbox
        return np.nonzero((bb[:, 0] <= x + r) & (bb[:, 2] >= x - r) & (bb[:, 1] <= y + r) & (bb[:, 3] >= y - r))[0]

    def floors(self, x, y):
        out = []
        for i in self.near(x, y, 0):
            if self.ptype[i] == 0 and self.contains(i, x, y):
                out.append((self.height(i, x, y), int(i)))
        return sorted(out)

    def locate(self, q, h=32, v=64, ground_only=True, usable=None):
        """Poly whose area holds (x, y) with floor within v of z, else the nearest within h (like a snap)."""
        x, y, z = q
        best = None
        for i in self.near(x, y, h):
            if ground_only and self.ptype[i] != 0:
                continue
            if usable is not None and not usable[i]:
                continue
            vv = self.polyverts[i]
            if self.contains(i, x, y):
                dh = 0.0
            else:
                dh = min(_seg(x, y, vv[k], vv[(k + 1) % len(vv)]) for k in range(len(vv)))
            if dh > h:
                continue
            dz = abs(self.height(i, x, y) - z)
            if dz > v:
                continue
            s = (dh, dz)
            if best is None or s < best[0]:
                best = (s, int(i))
        return best[1] if best else None

    # ---------------------------------------------------------------- graph
    def usable(self, extra_blocked=()):
        """Polys the server's default filter passes (include All^Disabled, flags != 0), minus extra_blocked."""
        u = (self.flags & (0xFFFF ^ DISABLED)) != 0
        u &= self.ptype == 0
        lk = self.ptype == 1
        u |= lk & ((self.flags & (0xFFFF ^ DISABLED)) != 0)
        u = u.copy()
        u[list(extra_blocked)] = False
        return u

    def edges(self):
        if self.links == "union" and len(self.loose):
            return np.concatenate([self.ia, self.loose[:, 0]]), np.concatenate([self.ib, self.loose[:, 1]])
        return self.ia, self.ib

    def graph(self, usable, weighted=False):
        ia, ib = self.edges()
        ok = usable[ia] & usable[ib]
        a, c = ia[ok], ib[ok]
        w = np.linalg.norm(self.cent[a] - self.cent[c], axis=1) + 1e-3 if weighted else np.ones(len(a))
        m = coo_matrix((w, (a, c)), shape=(self.n, self.n)).tocsr()
        return m.maximum(m.T)

    def components(self, usable):
        return connected_components(self.graph(usable), directed=False)[1]

    def route(self, usable, src, dst):
        d, pred = dijkstra(self.graph(usable, True), indices=src, return_predecessors=True)
        if not np.isfinite(d[dst]):
            return None, []
        out, t = [], dst
        while t >= 0 and t != src:
            out.append(int(t)); t = pred[t]
        return float(d[dst]), [src] + out[::-1]


def _overlap_slabs(amin, amax, bmin, bmax, px, py):
    """Detour's overlapSlabs (DetourNavMesh.cpp, findConnectingPolys with px 0.01, py = walkableClimb): two tile-border
    edges, given as (along, height) end points sorted by along, link when they overlap along the border and their heights
    cross or come within 2 * walkableClimb at either end of the overlap."""
    minx = max(amin[0] + px, bmin[0] + px)
    maxx = min(amax[0] - px, bmax[0] - px)
    if minx > maxx:
        return False
    ad = (amax[1] - amin[1]) / (amax[0] - amin[0]) if amax[0] != amin[0] else 0.0
    ak = amin[1] - ad * amin[0]
    bd = (bmax[1] - bmin[1]) / (bmax[0] - bmin[0]) if bmax[0] != bmin[0] else 0.0
    bk = bmin[1] - bd * bmin[0]
    dmin = (bd * minx + bk) - (ad * minx + ak)
    dmax = (bd * maxx + bk) - (ad * maxx + ak)
    if dmin * dmax < 0:
        return True
    thr = (py * 2) ** 2
    return dmin * dmin <= thr or dmax * dmax <= thr


def _seg(x, y, a, b):
    dx, dy = b[0] - a[0], b[1] - a[1]
    L = dx * dx + dy * dy
    t = 0 if L == 0 else max(0, min(1, ((x - a[0]) * dx + (y - a[1]) * dy) / L))
    return float(np.hypot(x - a[0] - t * dx, y - a[1] - t * dy))


def write_flags(src_path, dst_path, changes):
    """Copy src to dst with poly flags changed: changes = {flag_offset: (old, new)}. Asserts every old value."""
    b = bytearray(open(src_path, "rb").read())
    for off, (old, new) in changes.items():
        cur, = struct.unpack_from("<H", b, off)
        assert cur == old, (off, cur, old)
        struct.pack_into("<H", b, off, new)
    open(dst_path, "wb").write(bytes(b))


def poly_area(vv):
    """Horizontal area (sq game units) of a polygon's world vertices."""
    x, y = vv[:, 0], vv[:, 1]
    return float(abs(np.dot(x, np.roll(y, -1)) - np.dot(y, np.roll(x, -1))) / 2)
