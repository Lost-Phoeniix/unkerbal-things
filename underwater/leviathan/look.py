import bpy, sys, math, mathutils
args = sys.argv[sys.argv.index('--')+1:]
src, out = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
sc = bpy.context.scene
mesh = next(o for o in bpy.data.objects if o.type=='MESH')
arm = next(o for o in bpy.data.objects if o.type=='ARMATURE')
sc.render.engine = 'BLENDER_EEVEE' if 'BLENDER_EEVEE' in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties['engine'].enum_items] else 'BLENDER_EEVEE_NEXT'
sc.render.resolution_x, sc.render.resolution_y = 640, 400
w = bpy.data.worlds.new('w'); sc.world = w; w.use_nodes = True
w.node_tree.nodes['Background'].inputs[0].default_value = (0.25,0.27,0.3,1); w.node_tree.nodes['Background'].inputs[1].default_value = 1.0
s = bpy.data.objects.new('sun', bpy.data.lights.new('sun','SUN')); sc.collection.objects.link(s); s.data.energy = 3; s.rotation_euler = (0.6, 0.2, 0.8)
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam')); sc.collection.objects.link(cam); sc.camera = cam
dg = bpy.context.evaluated_depsgraph_get()
pts = [mesh.matrix_world @ mathutils.Vector(c) for c in mesh.bound_box]
ctr = sum(pts, mathutils.Vector()) / 8
size = max((p-ctr).length for p in pts)
views = {'top': (0.01,0,1)}
frames = [int(f) for f in args[2].split(',')] if len(args) > 2 else [1]
for f in frames:
    sc.frame_set(f)
    for n, d in views.items():
        dv = mathutils.Vector(d).normalized()
        cam.location = ctr + dv * size * 3.2
        cam.rotation_euler = (ctr - cam.location).to_track_quat('-Z','Y').to_euler()
        sc.render.filepath = out + '_%s_f%03d.png' % (n, f)
        bpy.ops.render.render(write_still=True)
print('CTR', tuple(round(x,3) for x in ctr), 'SIZE', round(size,3))
