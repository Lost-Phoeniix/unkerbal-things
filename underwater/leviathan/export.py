import bpy, sys
args = sys.argv[sys.argv.index('--')+1:]
src, out = args[0], args[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src)
for a in bpy.data.actions: a.name = 'swim'
sc = bpy.context.scene
sc.frame_start, sc.frame_end = 1, 500
sc.render.fps = 60
bpy.ops.export_scene.fbx(filepath=out, use_selection=False, object_types={'ARMATURE', 'MESH'}, path_mode='STRIP', embed_textures=False,
    add_leaf_bones=False, bake_anim=True, bake_anim_use_all_actions=False, bake_anim_use_nla_strips=False, bake_anim_simplify_factor=0.5)
print('EXPORTED')
