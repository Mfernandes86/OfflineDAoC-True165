"""A keep's client collision (the Godot client's converted 1.64 model) placed in region coordinates, and a
point-in-solid test on horizontal slices of it.

Placement follows game/zone_loader.gd: Basis(Vector3.UP, deg_to_rad(yaw)) * scale, origin = fixture pos (metres,
x east / y up / z south); 1 m = 40 game units; region = zone-local + zone offset (Zones.OffsetX/Y * 8192).
"""
import base64, json, math, os
import numpy as np

# The new client's zone folders (godot/content/zones). Set OFFLINE_DAOC_GODOT_ZONES to point at your copy; the default
# is the sibling layout of the dev tree (development-source/server/tools/navmesh_keepgate -> ../../../../source/new-client).
GODOT = os.environ.get("OFFLINE_DAOC_GODOT_ZONES") or os.path.normpath(os.path.join(
    os.path.dirname(os.path.abspath(__file__)), "..", "..", "..", "..", "source", "new-client", "godot", "content", "zones"))
U = 0.025


def keep_fixtures(zone_id):
    """[(model file stem, fixture dict)] for every keep model placed in the client zone."""
    z = json.load(open(os.path.join(GODOT, "zone%03d" % zone_id, "zone.json"), encoding="utf-8"))
    out = []
    for f in z["fixtures"]:
        fn = z["models"][f["m"]]["file"]
        if "keep" in os.path.basename(fn).lower():
            out.append((os.path.splitext(os.path.basename(fn))[0], f))
    return out


def load_collision(zone_id, model, fixture, offset, prefixes=("collision_",)):
    """(T, 3, 3) triangles in region coordinates (x, y, z) of the model's collision nodes."""
    d = json.load(open(os.path.join(GODOT, "zone%03d" % zone_id, "models", model + ".gltf"), encoding="utf-8"))
    raw = base64.b64decode(d["buffers"][0]["uri"].split(",", 1)[1])

    def acc(i):
        a = d["accessors"][i]
        bv = d["bufferViews"][a["bufferView"]]
        off = bv.get("byteOffset", 0) + a.get("byteOffset", 0)
        n = {"SCALAR": 1, "VEC3": 3}[a["type"]]
        dt = {5126: np.float32, 5123: np.uint16, 5125: np.uint32}[a["componentType"]]
        arr = np.frombuffer(raw, dt, a["count"] * n, off)
        return arr.reshape(-1, n) if n > 1 else arr

    th = math.radians(float(fixture["yaw"]))
    c, s = math.cos(th), math.sin(th)
    sc = float(fixture.get("scale", 1.0))
    px, py, pz = fixture["pos"]
    tris = []
    for nd in d["nodes"]:
        if "mesh" not in nd or not nd.get("name", "").startswith(prefixes):
            continue
        if "matrix" in nd or "rotation" in nd or "translation" in nd or "scale" in nd:
            raise ValueError("node transforms are not handled: %s" % nd.get("name"))
        for pr in d["meshes"][nd["mesh"]]["primitives"]:
            P = acc(pr["attributes"]["POSITION"]).astype(np.float64) * sc
            I = acc(pr["indices"]).astype(np.int64).reshape(-1, 3)
            x = P[:, 0] * c + P[:, 2] * s
            z = -P[:, 0] * s + P[:, 2] * c
            W = np.c_[(x + px) / U + offset[0], (z + pz) / U + offset[1], (P[:, 1] + py) / U]
            tris.append(W[I])
    return np.concatenate(tris) if tris else None   # None: the model has no collision nodes


def ray_hits(tris, x, y):
    """Sorted Z of every triangle the vertical line through (x, y) crosses."""
    a, b, c = tris[:, 0], tris[:, 1], tris[:, 2]
    v0 = c[:, :2] - a[:, :2]; v1 = b[:, :2] - a[:, :2]; v2 = np.array([x, y]) - a[:, :2]
    d00 = (v0 * v0).sum(1); d01 = (v0 * v1).sum(1); d11 = (v1 * v1).sum(1)
    d20 = (v2 * v0).sum(1); d21 = (v2 * v1).sum(1)
    den = d00 * d11 - d01 * d01
    ok = np.abs(den) > 1e-9
    den = np.where(ok, den, 1)
    u = (d11 * d20 - d01 * d21) / den
    v = (d00 * d21 - d01 * d20) / den
    m = ok & (u >= -1e-6) & (v >= -1e-6) & (u + v <= 1 + 1e-6)
    return np.sort(a[m, 2] + u[m] * (c[m, 2] - a[m, 2]) + v[m] * (b[m, 2] - a[m, 2]))


class Solid:
    """Point-in-solid on horizontal slices of a (mostly) closed collision mesh: ray parity in four directions, a point
    is inside when at least three of the four rays agree (open seams in the model then cannot flip a whole band)."""

    def __init__(self, tris):
        self.t = tris
        self.zlo = tris[:, :, 2].min(1)
        self.zhi = tris[:, :, 2].max(1)
        self.cache = {}

    def segs(self, z):
        z = round(z / 4) * 4 + 0.37          # off the model's round heights, so no vertex sits on the slice
        if z in self.cache:
            return self.cache[z]
        sel = np.nonzero((self.zlo <= z) & (self.zhi >= z))[0]
        A, B = [], []
        for t in self.t[sel]:
            d = t[:, 2] - z
            pts = []
            for a, b in ((0, 1), (1, 2), (2, 0)):
                if (d[a] <= 0 < d[b]) or (d[b] <= 0 < d[a]):
                    f = d[a] / (d[a] - d[b])
                    pts.append(t[a, :2] + f * (t[b, :2] - t[a, :2]))
            if len(pts) >= 2:
                A.append(pts[0]); B.append(pts[1])
        r = (np.array(A).reshape(-1, 2), np.array(B).reshape(-1, 2))
        self.cache[z] = r
        return r

    @staticmethod
    def _parity(A, B, x, y, sign):
        ya, yb = A[:, 1], B[:, 1]
        m = (ya > y) != (yb > y)
        if not m.any():
            return 0
        xa, xb, ya, yb = A[m, 0], B[m, 0], ya[m], yb[m]
        xi = xa + (y - ya) * (xb - xa) / (yb - ya)
        return int(((xi - x) * sign > 0).sum()) % 2

    def votes(self, x, y, z):
        A, B = self.segs(z)
        if len(A) == 0:
            return 0
        As, Bs = A[:, ::-1], B[:, ::-1]
        x, y = x + 0.013, y + 0.017
        return (self._parity(A, B, x, y, 1) + self._parity(A, B, x, y, -1) +
                self._parity(As, Bs, y, x, 1) + self._parity(As, Bs, y, x, -1))

    def inside(self, x, y, z):
        return self.votes(x, y, z) >= 3
