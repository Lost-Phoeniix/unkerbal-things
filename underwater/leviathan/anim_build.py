# Leviathan clips, written into the open leviathan_rig.blend (run from Blender's text editor; needs the split spine).
# The body is a line of rigid pieces; every pose comes from one function h(y): how far the backbone is pushed sideways at the place y along the fish.
#   swim / sprint  a wave running from head to tail (carangiform: small at the head, least a quarter behind the nose, largest at the tail)
#   turn_left/right  the backbone lies on the arc of the circle the fish swims (additive: frame 0 is the rest pose)
#   attack         jaw open, skull lifted, fins flared (additive)
# World axes in Blender: nose -Y, back +Z, the fish's left +X. 60 fps; one tail beat = 60 frames, so the game sets beats per second as the clip speed.
import bpy, math
from mathutils import Quaternion, Vector

arm = bpy.data.objects['Armature']
MW = arm.matrix_world
X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
NOSE, TAIL, LEN, MID = -0.371, 0.802, 1.173, 0.215
SPINE = ['Bone', 'Bone.a', 'Bone.b', 'Bone.001', 'Bone.001b', 'Bone.002']
FRONT = ['Bone.005', 'Bone.008', 'Bone.010', 'Bone.012']          # head, pectoral roots, belly: one rigid block in front of the pivot
JY = [(MW @ arm.data.bones[n].head_local).y for n in SPINE]
PIVOT = JY[0]
FIN0 = (MW @ arm.data.bones['Bone.003'].head_local).y
REST = {b.name: (MW @ b.matrix_local).to_quaternion() for b in arm.data.bones}
TOLOCAL = {b.name: (MW.to_3x3() @ b.matrix_local.to_3x3()).inverted() for b in arm.data.bones}
last = {}

def deg(a):
    return math.radians(a)

def chord(h, ya, yb):
    return Quaternion(Z, -math.atan2(h(yb) - h(ya), yb - ya))

# h: sideways push of the backbone at y. hfin: the same for the two tail lobes (they trail the body). Returns the world turn of every piece.
def body(h, hfin_up=None, hfin_down=None, look=0.0):
    q = {}
    front = Quaternion(Z, -look) @ chord(h, NOSE, PIVOT)
    for n in FRONT:
        q[n] = front
    for i, n in enumerate(SPINE):
        q[n] = chord(h, JY[i], JY[i + 1] if i + 1 < len(SPINE) else FIN0)
    q['Bone.003'] = chord(hfin_up or h, FIN0, TAIL)
    q['Bone.004'] = chord(hfin_down or h, FIN0, TAIL)
    return q, front, h(PIVOT)

# fins and jaw, in the frame of the front block. Angles in degrees; spread: away from the body, lift: tip up.
def trim(q, front, pecL=0, pecR=0, liftL=0, liftR=0, venL=0, venR=0, sway=0, jaw=0, skull=0):
    q['Bone.009'] = front @ Quaternion(Z, -deg(pecL)) @ Quaternion(X, deg(liftL))
    q['Bone.011'] = front @ Quaternion(Z, deg(pecR)) @ Quaternion(X, deg(liftR))
    q['Bone.016'] = front @ Quaternion(Y, deg(sway - venL))
    q['Bone.015'] = front @ Quaternion(Y, deg(sway + venR))
    q['Bone.007'] = front @ Quaternion(X, deg(jaw))
    q['Bone.006'] = front @ Quaternion(X, -deg(skull))

def key(q, shift, frame):
    full = {}
    for b in arm.data.bones:
        pq = full[b.parent.name] if b.parent else Quaternion()
        w = q.get(b.name, pq)
        full[b.name] = w
        pb = arm.pose.bones[b.name]
        pb.rotation_mode = 'QUATERNION'
        r = REST[b.name].inverted() @ (pq.inverted() @ w) @ REST[b.name]
        if b.name in last and last[b.name].dot(r) < 0:
            r.negate()
        last[b.name] = r.copy()
        pb.rotation_quaternion = r
        pb.location = TOLOCAL[b.name] @ Vector((shift, 0, 0)) if b.parent is None else (0, 0, 0)
        pb.keyframe_insert('rotation_quaternion', frame=frame)
        if b.parent is None:
            pb.keyframe_insert('location', frame=frame)

def new_action(name):
    old = bpy.data.actions.get(name)
    if old:
        bpy.data.actions.remove(old)
    a = bpy.data.actions.new(name)
    a.use_fake_user = True
    arm.animation_data.action = a
    last.clear()
    return a

# the wave: t in tail beats. amp: tail tip amplitude as a share of the body length.
def wave(amp, t, lag=0.0):
    def h(y):
        x = (y - NOSE) / LEN
        return amp * LEN * (0.2 - 0.8 * x + 1.6 * x * x) * math.sin(2 * math.pi * (x / 1.05 - (t - lag)))
    return h

def swim_clip(name, amp, pec, pec_swing, lift, ven, sway, jaw):
    new_action(name)
    for f in range(0, 121):
        t = f / 60.0
        q, front, shift = body(wave(amp, t), wave(amp, t, 0.06), wave(amp, t, 0.10))
        s = math.sin(math.pi * t)
        trim(q, front, pecL=pec + pec_swing * s, pecR=pec - pec_swing * s, liftL=lift * math.sin(math.pi * t + 1.2), liftR=-lift * math.sin(math.pi * t + 1.2),
             venL=ven, venR=ven, sway=sway * math.sin(2 * math.pi * t - 2.0), jaw=jaw * (1 + s))
        key(q, shift, f)

def arc(total_deg, side):
    k = side * math.radians(total_deg) / LEN
    def h(y):
        return 0.5 * k * (y - MID) ** 2
    return h

def straight(y):
    return 0.0

def ease(u):
    return u * u * (3 - 2 * u)

def turn_clip(name, side):
    new_action(name)
    for f in range(0, 21):
        u = ease(f / 20.0)
        q, front, shift = body(arc(100 * u, side), look=deg(6) * u * side)
        inner, outer = 55 * u, -5 * u
        trim(q, front, pecL=inner if side > 0 else outer, pecR=outer if side > 0 else inner, liftL=-10 * u if side > 0 else 0, liftR=0 if side > 0 else -10 * u, sway=-12 * u * side)
        key(q, shift, f)

def attack_clip():
    new_action('attack')
    for f in range(0, 21):
        u = ease(f / 20.0)
        q, front, shift = body(straight)
        trim(q, front, pecL=50 * u, pecR=50 * u, liftL=-15 * u, liftR=-15 * u, venL=28 * u, venR=28 * u, jaw=40 * u, skull=13 * u)
        key(q, shift, f)

swim_clip('swim', 0.165, 22, 7, 9, 6, 6, 2.5)
swim_clip('sprint', 0.22, -4, 2, 0, -5, 3, 0)
turn_clip('turn_left', 1)
turn_clip('turn_right', -1)
attack_clip()
arm.animation_data.action = bpy.data.actions['swim']
bpy.context.scene.render.fps = 60
bpy.context.scene.frame_start, bpy.context.scene.frame_end = 0, 120
print('actions', [(a.name, tuple(a.frame_range)) for a in bpy.data.actions])
