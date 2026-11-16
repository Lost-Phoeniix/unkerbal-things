import bpy, sys, mathutils, numpy as np
args = sys.argv[sys.argv.index('--')+1:]
src, outdir = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
sc = bpy.context.scene
mesh = next(o for o in bpy.data.objects if o.type=='MESH')
img = next(i for i in bpy.data.images if i.name == 'texture_pbr_20250901.png')
img.scale(2048, 2048)
px = np.empty(2048*2048*4, dtype=np.float32); img.pixels.foreach_get(px); px = px.reshape(-1,4)
rgb = px[:, :3]
L = (0.2126*rgb[:,0] + 0.7152*rgb[:,1] + 0.0722*rgb[:,2]).clip(1e-4, 1)
def variant(gamma, gain, desat):
    k = gain * L ** (gamma - 1.0)
    out = rgb * k[:, None]
    # the darker a pixel ends up, the more its colour drains away
    lo = (0.2126*out[:,0] + 0.7152*out[:,1] + 0.0722*out[:,2])[:, None]
    keep = (1 - desat) + desat * np.clip(lo / 0.35, 0, 1)
    out = lo + (out - lo) * keep
    return np.concatenate([out.clip(0, 1), px[:, 3:]], axis=1)
looks = {'orig': None, 'A': variant(2.4, 0.85, 0.35), 'B': variant(3.6, 0.8, 0.6)}
eng = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items]
sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in eng else 'BLENDER_EEVEE_NEXT'
sc.render.resolution_x, sc.render.resolution_y = 1100, 620
sc.view_settings.view_transform = 'Standard'
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
bg = w.node_tree.nodes['Background']
s = bpy.data.objects.new('sun', bpy.data.lights.new('sun','SUN')); sc.collection.objects.link(s); s.rotation_euler = (0.5, 0.15, 0.9)
spot = bpy.data.objects.new('lamp', bpy.data.lights.new('lamp','SPOT')); sc.collection.objects.link(spot)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
pts = [mesh.matrix_world @ mathutils.Vector(c) for c in mesh.bound_box]
ctr = sum(pts, mathutils.Vector()) / 8; size = max((p-ctr).length for p in pts)
d = mathutils.Vector((1, -0.55, 0.18)).normalized()
cam.location = ctr + d * size * 3.1
cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z','Y').to_euler()
spot.location = cam.location + mathutils.Vector((0, 0, size*0.6)); spot.rotation_euler = (ctr - spot.location).to_track_quat('-Z','Y').to_euler()
sc.frame_set(60)
work = bpy.data.images.new('work', 2048, 2048, alpha=True)
for n, data in looks.items():
    if data is not None:
        work.pixels.foreach_set(data.ravel()); work.update()
        for m in bpy.data.materials:
            for node in m.node_tree.nodes:
                if node.type == 'TEX_IMAGE' and node.image == img: node.image = work
        work.filepath_raw = outdir + '\leviathan_%s.png' % n; work.file_format = 'PNG'; work.save()
    # two lightings: neutral grey studio, and the water at depth with the craft's lamp
    for mode in ('studio', 'deep'):
        if mode == 'studio':
            bg.inputs[0].default_value = (0.3, 0.32, 0.35, 1); bg.inputs[1].default_value = 1.0; s.data.energy = 3.0; spot.data.energy = 0
        else:
            bg.inputs[0].default_value = (0.005, 0.02, 0.03, 1); bg.inputs[1].default_value = 1.0; s.data.energy = 0.15; spot.data.energy = 60; spot.data.spot_size = 1.2; spot.data.color = (0.9, 0.95, 1)
        sc.render.filepath = outdir + '\shot_%s_%s.png' % (n, mode)
        bpy.ops.render.render(write_still=True)
print('DONE')
