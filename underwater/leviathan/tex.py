import bpy, sys, mathutils, numpy as np
args = sys.argv[sys.argv.index('--')+1:]
src, outdir = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
arm = next(o for o in bpy.data.objects if o.type=='ARMATURE')
mesh = next(o for o in bpy.data.objects if o.type=='MESH')
for b in arm.data.bones:
    print('BW', b.name, tuple(round(x,3) for x in arm.matrix_world @ b.head_local), tuple(round(x,3) for x in arm.matrix_world @ b.tail_local))
print('ARMROT', tuple(round(x,3) for x in arm.rotation_euler), 'MESHMAT', [tuple(round(x,3) for x in r) for r in mesh.matrix_world])
pts = [mesh.matrix_world @ mathutils.Vector(c) for c in mesh.bound_box]
print('BOUNDS', tuple(round(min(p[i] for p in pts),3) for i in range(3)), tuple(round(max(p[i] for p in pts),3) for i in range(3)))
nrm = bpy.data.images['texture_pbr_20250901_normal.png']; nrm.scale(2048, 2048)
nrm.filepath_raw = outdir + '\leviathan_normal.png'; nrm.file_format = 'PNG'; nrm.save()
for v in ('A', 'B'):
    im = bpy.data.images.load(outdir + '\leviathan_%s.png' % v)
    px = np.empty(2048*2048*4, dtype=np.float32); im.pixels.foreach_get(px); px = px.reshape(-1, 4)
    L = 0.2126*px[:,0] + 0.7152*px[:,1] + 0.0722*px[:,2]
    # glow only where the skin is brightest: the spots and the eyes
    g = np.clip((L - 0.22) / 0.25, 0, 1) ** 1.5
    out = np.concatenate([px[:, :3] * g[:, None], np.ones((px.shape[0], 1), np.float32)], axis=1)
    em = bpy.data.images.new('em' + v, 2048, 2048); em.pixels.foreach_set(out.ravel()); em.update()
    em.filepath_raw = outdir + '\leviathan_%s_glow.png' % v; em.file_format = 'PNG'; em.save()
    print('GLOWFRAC', v, float((g > 0.05).mean()))
