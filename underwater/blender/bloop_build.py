# Builds the Laythe Bloop in the running Blender: a deep-sea leviathan, about 48 m long, head towards +Y.
# Everything is generated here, no outside asset. Bones: Spine0..Spine11 (head to tail), Jaw, FinL, FinR, Lure0..Lure3. Writes bloop.fbx and bloop_albedo.png beside this file.
import bpy, bmesh, math, os, random
import numpy as np
from mathutils import Vector

OUT = os.path.dirname(bpy.data.filepath) or os.path.expanduser("~")   # unsaved file: the home folder, never Blender's own
random.seed(7)
LEN = 48.0
HEAD_Y = 19.0          # the snout tip
SEGS = 12
JAW_HINGE_Y = HEAD_Y - 7.2


def smooth(a, b, x):
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


def lerp_table(tab, u):
    for i in range(len(tab) - 1):
        if u <= tab[i + 1][0]:
            a, b = tab[i], tab[i + 1]
            t = (u - a[0]) / max(1e-6, b[0] - a[0])
            t = t * t * (3 - 2 * t)
            return tuple(a[k] + (b[k] - a[k]) * t for k in range(1, len(a)))
    return tab[-1][1:]


# u along the body 0 snout .. 1 tail tip: half width, half height, centre height
BODY = [
    (0.00, 0.30, 0.25, 0.75), (0.03, 1.55, 0.95, 1.05), (0.09, 2.85, 1.85, 1.35), (0.16, 3.65, 2.75, 1.25),
    (0.26, 3.45, 3.25, 0.70), (0.38, 2.80, 3.05, 0.30), (0.52, 2.10, 2.55, 0.10), (0.66, 1.45, 1.95, 0.05),
    (0.80, 0.85, 1.35, 0.05), (0.92, 0.40, 0.85, 0.05), (1.00, 0.16, 0.42, 0.05),
]
RINGS, SIDES = 110, 28


def noise3(x, y, z):
    return (math.sin(x * 1.9 + y * 0.7) * math.sin(y * 1.3 + z * 2.1) + 0.5 * math.sin(x * 4.3 - z * 3.7 + y * 2.9) * math.sin(y * 3.1 + x * 1.1))


def body_mesh():
    bm = bmesh.new()
    uv_data = []
    rings = []
    for r in range(RINGS + 1):
        u = r / RINGS
        y = HEAD_Y - u * LEN
        w, h, cz = lerp_table(BODY, u)
        ring = []
        for s in range(SIDES):
            a = 2 * math.pi * s / SIDES
            ca, sa = math.cos(a), math.sin(a)
            # flat belly, keeled back behind the head
            keel = 1.0 + 0.22 * max(0.0, sa) ** 3 * smooth(0.15, 0.5, u)
            flat = 1.0 - 0.18 * max(0.0, -sa) * (1 - smooth(0.35, 0.8, u))
            x = ca * w
            z = sa * h * keel * flat + cz
            # lumps, heavy ribs behind the head, sunken flank
            bump = 0.05 * noise3(x * 1.2, y * 0.8, z * 1.2) * min(w, h)
            rib = 0.045 * math.sin(u * 95.0) * smooth(0.13, 0.2, u) * (1 - smooth(0.42, 0.55, u)) * abs(ca) * w
            # the mouth: a deep open wedge on the head, the jaw is its own mesh
            mouth = 0.0
            if u < 0.17 and sa < 0.05:
                t = (1 - smooth(0.11, 0.17, u))
                z = z + (cz + 0.10 - z) * t * smooth(0.05, -0.8, sa)
            ring.append(bm.verts.new((x + ca * (bump + rib + mouth), y, z + sa * (bump + rib))))
            uv_data.append((s / SIDES, u))
        rings.append(ring)
    uvl = bm.loops.layers.uv.new("UV")
    for r in range(RINGS):
        for s in range(SIDES):
            s2 = (s + 1) % SIDES
            f = bm.faces.new((rings[r][s], rings[r][s2], rings[r + 1][s2], rings[r + 1][s]))
            us = [s / SIDES, (s + 1) / SIDES, (s + 1) / SIDES, s / SIDES]
            vs = [r / RINGS, r / RINGS, (r + 1) / RINGS, (r + 1) / RINGS]
            for l, a, b in zip(f.loops, us, vs):
                l[uvl].uv = (a * 0.5, 1 - b)   # body takes the left half of the texture
    bm.faces.new(rings[0][::-1])
    bm.faces.new(rings[-1])
    me = bpy.data.meshes.new("body")
    bm.to_mesh(me)
    bm.free()
    return me


def add_shape(bm, uvl, verts, faces, uvs):
    vs = [bm.verts.new(v) for v in verts]
    for f, fu in zip(faces, uvs):
        try:
            face = bm.faces.new([vs[i] for i in f])
        except ValueError:
            continue
        for l, uv in zip(face.loops, fu):
            l[uvl].uv = uv


def cone(bm, uvl, base, tip, r, sides, uv):
    base = Vector(base); tip = Vector(tip)
    d = (tip - base).normalized()
    a = d.orthogonal().normalized(); b = d.cross(a)
    v = [tuple(base + (a * math.cos(2 * math.pi * i / sides) + b * math.sin(2 * math.pi * i / sides)) * r) for i in range(sides)] + [tuple(tip)]
    # teeth curve backward: one mid ring
    f = [(i, (i + 1) % sides, sides) for i in range(sides)]
    add_shape(bm, uvl, v, f, [[uv, uv, uv]] * len(f))


def jaw_and_teeth():
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UV")
    # lower jaw: an open shell hanging from the hinge, closed pose
    N, S = 22, 14
    rows = []
    for i in range(N + 1):
        t = i / N
        y = HEAD_Y + 0.9 - t * 9.2
        w = 0.55 + 2.75 * smooth(0, 0.75, t)
        depth = 0.5 + 1.85 * smooth(0, 0.65, t)
        row = []
        for s in range(S + 1):
            a = math.pi * s / S
            row.append(bm.verts.new((math.cos(a) * w, y, 0.55 - math.sin(a) * depth)))
        rows.append(row)
    for i in range(N):
        for s in range(S):
            f = bm.faces.new((rows[i][s], rows[i + 1][s], rows[i + 1][s + 1], rows[i][s + 1]))
            for l, (a, b) in zip(f.loops, [(s / S, i / N), (s / S, (i + 1) / N), ((s + 1) / S, (i + 1) / N), ((s + 1) / S, i / N)]):
                l[uvl].uv = (0.5 + a * 0.24, 0.5 + b * 0.24)
    tooth_uv = (0.87, 0.87)
    # needle teeth, irregular, far too long, along both jaw rims and the roof
    for side in (-1, 1):
        for i in range(17):
            t = 0.04 + 0.92 * i / 16
            y = HEAD_Y + 0.9 - t * 8.0
            w = (0.55 + 2.75 * smooth(0, 0.75, t)) * side
            L = random.uniform(0.7, 1.9) * (1.25 if i % 4 == 1 else 0.85)
            cone(bm, uvl, (w * 0.97, y, 0.55), (w * random.uniform(0.72, 0.95), y - 0.25, 0.55 + L), 0.11, 5, tooth_uv)
    me = bpy.data.meshes.new("jaw")
    bm.to_mesh(me); bm.free()
    upper = bpy.data.meshes.new("upper")
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UV")
    for side in (-1, 1):
        for i in range(15):
            t = 0.02 + 0.95 * i / 14
            y = HEAD_Y + 0.05 - t * 7.2
            w = (0.7 + 2.2 * smooth(0, 0.8, t)) * side
            L = random.uniform(0.8, 2.3)
            cone(bm, uvl, (w, y, 1.05), (w * random.uniform(0.75, 0.97), y - 0.3, 1.05 - L), 0.12, 5, tooth_uv)
    bm.to_mesh(upper); bm.free()
    return me, upper


def fin_mesh():
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UV")
    fin_uv = lambda a, b: (0.5 + a * 0.2, 0.2 * b + 0.1)

    def fin(root_a, root_b, spokes):
        vs = [root_a, root_b] + spokes
        n = len(spokes)
        fs = [(0, 1, 2)] + [(1, 2 + i + 1, 2 + i) if False else (1, 2 + i, 2 + i + 1) for i in range(n - 1)]
        uvs = [[fin_uv(0, 0), fin_uv(1, 0), fin_uv(0, 1)]] + [[fin_uv(1, 0), fin_uv(i / n, 1), fin_uv((i + 1) / n, 1)] for i in range(n - 1)]
        add_shape(bm, uvl, vs, fs, uvs)
        rv = [root_b, root_a] + spokes[::-1]
        add_shape(bm, uvl, rv, fs, uvs)

    # tall ragged dorsal sail along the back
    for i in range(24):
        u0 = 0.25 + 0.60 * i / 24; u1 = 0.25 + 0.60 * (i + 1) / 24
        _, h0, c0 = lerp_table(BODY, u0); _, h1, c1 = lerp_table(BODY, u1)
        y0, y1 = HEAD_Y - u0 * LEN, HEAD_Y - u1 * LEN
        H0 = (1.0 + 2.8 * math.sin(math.pi * (i / 24)) ** 0.7) * random.uniform(0.75, 1.15)
        H1 = (1.0 + 2.8 * math.sin(math.pi * ((i + 1) / 24)) ** 0.7) * random.uniform(0.75, 1.15)
        z0 = c0 + h0 * 1.12; z1 = c1 + h1 * 1.12
        add_shape(bm, uvl, [(0, y0, z0 - 0.2), (0, y1, z1 - 0.2), (0, y1 - 0.5, z1 + H1), (0, y0 - 0.5, z0 + H0)], [(0, 1, 2, 3), (3, 2, 1, 0)], [[fin_uv(0, 0), fin_uv(.1, 0), fin_uv(.1, 1), fin_uv(0, 1)]] * 2)
    # tail fan
    ty = HEAD_Y - LEN
    fin((0, ty + 3.4, 0.35), (0, ty + 0.2, 0.05), [(0, ty - 1.2, 6.0), (0, ty - 4.5, 3.8), (0, ty - 5.5, 0.1), (0, ty - 4.3, -3.4), (0, ty - 1.0, -5.2), (0, ty + 1.8, -1.2)])
    me = bpy.data.meshes.new("fins")
    bm.to_mesh(me); bm.free()
    pec = {}
    for name, side in (("finL", -1), ("finR", 1)):
        bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UV")
        y0 = HEAD_Y - 11.8
        vs = [(side * 3.3, y0, 0.2), (side * 3.0, y0 - 2.8, -0.3)]
        sp = [(side * 8.4, y0 + 0.8, -1.2), (side * 11.2, y0 - 2.2, -2.6), (side * 9.8, y0 - 6.2, -3.6), (side * 6.2, y0 - 7.0, -3.1), (side * 3.6, y0 - 4.6, -1.4)]
        fin(vs[0], vs[1], sp)
        pm = bpy.data.meshes.new(name)
        bm.to_mesh(pm); bm.free()
        pec[name] = pm
    return me, pec


def eyes_mesh():
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UV")
    for side in (-1, 1):
        r = bmesh.ops.create_uvsphere(bm, u_segments=12, v_segments=8, radius=0.42)
        for v in r["verts"]:
            v.co += Vector((side * 2.42, HEAD_Y - 5.0, 2.15))
        for f in {f for v in r["verts"] for f in v.link_faces}:
            for l in f.loops:
                l[uvl].uv = (0.97, 0.97)
    me = bpy.data.meshes.new("eyes")
    bm.to_mesh(me); bm.free()
    return me


def lure_mesh():
    # the stalk, thin and crooked, from the forehead forward; the bulb itself is made at runtime so it can glow
    bm = bmesh.new(); uvl = bm.loops.layers.uv.new("UV")
    pts = [Vector((0, HEAD_Y - 7.0, 3.6)), Vector((0.1, HEAD_Y - 4.5, 6.4)), Vector((-0.1, HEAD_Y - 0.8, 7.2)), Vector((0.05, HEAD_Y + 2.3, 5.2))]
    for i in range(3):
        cone(bm, uvl, pts[i], pts[i + 1], 0.20 - i * 0.05, 6, (0.87, 0.72))
    me = bpy.data.meshes.new("stalk")
    bm.to_mesh(me); bm.free()
    return me, pts


def make_albedo():
    # 1024 x 1024: left half body (u around, v along), jaw and fins on the right, teeth and eye in the top right corner. Near black, wet, mottled, scarred.
    W = 1024
    rng = np.random.RandomState(11)

    def fbm(w, h, base):
        out = np.zeros((h, w), np.float32)
        amp = 1.0
        for o in range(6):
            n = base * 2 ** o
            g = rng.rand(n + 1, n * 2 + 1)
            y = np.linspace(0, n, h); x = np.linspace(0, n * 2, w)
            xi = x.astype(int).clip(0, n * 2 - 1); yi = y.astype(int).clip(0, n - 1)
            fx = (x - xi)[None, :]; fy = (y - yi)[:, None]
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy)
            a = g[np.ix_(yi, xi)]; b = g[np.ix_(yi, xi + 1)]; c = g[np.ix_(yi + 1, xi)]; d = g[np.ix_(yi + 1, xi + 1)]
            out += amp * (a * (1 - fx) * (1 - fy) + b * fx * (1 - fy) + c * (1 - fx) * fy + d * fx * fy)
            amp *= 0.55
        return out / out.max()

    n = fbm(W, W, 4)
    n2 = fbm(W, W, 16)
    # image row 0 is the top: v = 1 - row/W; body u along the body = 1 - uv.y, so row/W is the body position
    img = np.zeros((W, W, 3), np.float32)
    around = np.linspace(0, 1, W // 2)[None, :]
    updown = np.sin(around * 2 * np.pi)            # +1 back, -1 belly
    belly = np.clip(-updown, 0, 1)
    base = 0.035 + 0.055 * belly + 0.05 * n[:, :W // 2] * n2[:, :W // 2]
    col = np.stack([base * 0.78, base * 0.95, base * 1.12], -1)
    # pale scars: thin bright lines where noise crosses a level
    sc = np.abs(n2[:, :W // 2] - 0.42) < 0.0035
    col[sc] += np.array([0.20, 0.20, 0.18])
    # blotches like old wounds
    bl = fbm(W, W, 7)[:, :W // 2] > 0.70
    col[bl] += np.array([0.05, 0.04, 0.035])
    img[:, :W // 2] = col
    img[:, W // 2:] = np.stack([0.03 + 0.05 * n2[:, W // 2:]] * 3, -1) * np.array([0.85, 0.95, 1.1])
    # jaw inside: wet dark red, uv (0.5..0.74, 0.5..0.74)
    r0, r1, c0, c1 = int(W * 0.26), int(W * 0.50), int(W * 0.50), int(W * 0.74)
    img[r0:r1, c0:c1] = np.stack([0.10 + 0.13 * n2[r0:r1, c0:c1], 0.018 + 0.02 * n2[r0:r1, c0:c1], 0.022 + 0.02 * n2[r0:r1, c0:c1]], -1)
    # teeth: dirty bone, eye: dead milky
    img[int(W * 0.08):int(W * 0.18), int(W * 0.82):int(W * 0.92)] = (0.62, 0.58, 0.46)
    img[int(W * 0.23):int(W * 0.33), int(W * 0.82):int(W * 0.92)] = (0.12, 0.09, 0.08)
    img[0:int(W * 0.06), int(W * 0.94):] = (0.55, 0.60, 0.55)
    px = np.ones((W, W, 4), np.float32)
    px[:, :, :3] = np.clip(img[::-1], 0, 1)
    im = bpy.data.images.get("bloop_albedo") or bpy.data.images.new("bloop_albedo", W, W, alpha=False)
    im.pixels.foreach_set(px.ravel())
    im.filepath_raw = os.path.join(OUT, "bloop_albedo.png")
    im.file_format = 'PNG'
    im.save()
    return im


def build():
    for o in list(bpy.data.objects):
        bpy.data.objects.remove(o)
    col = bpy.context.scene.collection
    meshes = {"body": body_mesh()}
    meshes["jaw"], meshes["upper"] = jaw_and_teeth()
    meshes["fins"], pec = fin_mesh()
    meshes.update(pec)
    meshes["eyes"] = eyes_mesh()
    meshes["stalk"], lure_pts = lure_mesh()

    # armature
    arm = bpy.data.armatures.new("BloopRig")
    ao = bpy.data.objects.new("Bloop", arm)
    col.objects.link(ao)
    bpy.context.view_layer.objects.active = ao
    ao.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    spine = []
    for i in range(SEGS):
        b = arm.edit_bones.new("Spine%d" % i)
        u0, u1 = i / SEGS, (i + 1) / SEGS
        _, _, c0 = lerp_table(BODY, u0); _, _, c1 = lerp_table(BODY, u1)
        b.head = (0, HEAD_Y - u0 * LEN, c0)
        b.tail = (0, HEAD_Y - u1 * LEN, c1)
        if spine:
            b.parent = spine[-1]; b.use_connect = True
        spine.append(b)
    j = arm.edit_bones.new("Jaw"); j.head = (0, JAW_HINGE_Y, 0.55); j.tail = (0, HEAD_Y, 0.55); j.parent = spine[0]
    for name, side in (("FinL", -1), ("FinR", 1)):
        f = arm.edit_bones.new(name); f.head = (side * 3.2, HEAD_Y - 13, 0.0); f.tail = (side * 10.5, HEAD_Y - 15, -2.6); f.parent = spine[3]
    prev = spine[0]
    for i in range(3):
        l = arm.edit_bones.new("Lure%d" % i); l.head = lure_pts[i]; l.tail = lure_pts[i + 1]; l.parent = prev
        prev = l
    tip = arm.edit_bones.new("LureTip"); tip.head = lure_pts[3]; tip.tail = lure_pts[3] + Vector((0, 0.6, 0)); tip.parent = prev
    bpy.ops.object.mode_set(mode='OBJECT')

    # join into one skinned mesh with automatic weights by bone distance
    bm = bmesh.new()
    groups = []
    uv = bm.loops.layers.uv.new("UV")
    for key, me in meshes.items():
        m = bmesh.new(); m.from_mesh(me)
        muv = m.loops.layers.uv.active
        vmap = {}
        for v in m.verts:
            vmap[v] = bm.verts.new(v.co); groups.append(key)
        for f in m.faces:
            try:
                nf = bm.faces.new([vmap[v] for v in f.verts])
            except ValueError:
                continue
            for l, ol in zip(nf.loops, f.loops):
                l[uv].uv = ol[muv].uv if muv else (0, 0)
        m.free()
    bmesh.ops.recalc_face_normals(bm, faces=[f for f in bm.faces])
    me = bpy.data.meshes.new("BloopMesh")
    bm.to_mesh(me); bm.free()
    for p in me.polygons:
        p.use_smooth = True
    ob = bpy.data.objects.new("BloopBody", me)
    col.objects.link(ob)
    ob.parent = ao
    mod = ob.modifiers.new("arm", 'ARMATURE'); mod.object = ao
    vg = {b.name: ob.vertex_groups.new(name=b.name) for b in arm.bones}
    bones = {b.name: (Vector(b.head_local), Vector(b.tail_local)) for b in arm.bones}
    spine_names = ["Spine%d" % i for i in range(SEGS)]
    for v, key in zip(me.vertices, groups):
        if key == "jaw":
            vg["Jaw"].add([v.index], 1.0, 'REPLACE')
        elif key == "finL":
            vg["FinL"].add([v.index], 1.0, 'REPLACE')
        elif key == "finR":
            vg["FinR"].add([v.index], 1.0, 'REPLACE')
        elif key == "stalk":
            best = min(("Lure0", "Lure1", "Lure2"), key=lambda n: (v.co - (bones[n][0] + bones[n][1]) / 2).length)
            vg[best].add([v.index], 1.0, 'REPLACE')
        elif key in ("upper", "eyes"):
            vg["Spine0"].add([v.index], 1.0, 'REPLACE')
        else:
            # smooth blend of the two nearest spine bones along the body
            pos = (HEAD_Y - v.co.y) / LEN * SEGS - 0.5
            i0 = int(math.floor(pos)); t = pos - i0
            a = max(0, min(SEGS - 1, i0)); b = max(0, min(SEGS - 1, i0 + 1))
            if a == b:
                vg[spine_names[a]].add([v.index], 1.0, 'REPLACE')
            else:
                vg[spine_names[a]].add([v.index], 1 - t, 'REPLACE')
                vg[spine_names[b]].add([v.index], t, 'REPLACE')
    # material for looking at it in Blender
    im = make_albedo()
    mat = bpy.data.materials.get("bloop") or bpy.data.materials.new("bloop")
    mat.use_nodes = True
    bsdf = next(n for n in mat.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage"); tex.image = im
    mat.node_tree.links.new(tex.outputs[0], bsdf.inputs[0])
    bsdf.inputs["Roughness"].default_value = 0.35
    me.materials.append(mat)
    for k, m in list(meshes.items()):
        bpy.data.meshes.remove(m)
    print("verts", len(me.vertices), "tris", sum(len(p.vertices) - 2 for p in me.polygons), "bones", len(arm.bones))
    print("lure tip", tuple(round(c, 2) for c in lure_pts[3]))
    return ao, ob


def export():
    bpy.ops.object.select_all(action='DESELECT')
    for n in ("Bloop", "BloopBody"):
        bpy.data.objects[n].select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "bloop.fbx"), use_selection=True, object_types={'ARMATURE', 'MESH'}, add_leaf_bones=False, bake_anim=False, mesh_smooth_type='FACE', path_mode='COPY', apply_unit_scale=True)
    print("exported")


build()
export()
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "bloop.blend"))
