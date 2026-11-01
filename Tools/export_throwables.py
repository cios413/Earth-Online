"""Run with Blender 4.2 in background; never saves or overwrites the source blend.

Exports evaluated collection/object meshes in Unity coordinates, baked base colors
and a manifest consumed by ThrowableAssetBuilder. Prefix a selector with @ to
export one exact object. Pass -- --only JellyCube to update only that slot.
"""
import bpy
import json
import math
import os
import sys
from mathutils import Matrix, Vector

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
OUT = os.path.join(ROOT, 'Assets', 'BodyThrow', 'Models', 'Throwables')
os.makedirs(OUT, exist_ok=True)

SPECS = [
    ('Sticky Hand', 'StickyHand', '1号 · 粘粘手', 0.24, Matrix(((1, 0, 0), (0, 1, 0), (0, 0, -1)))),
    ('Human Brain', 'Brain', '2号 · 大脑', 0.24, Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))),
    ('@立方体.001', 'JellyCube', '3号 · 方块', 0.36, Matrix(((1, 0, 0), (0, 0, 1), (0, 1, 0)))),
    ('Arrow', 'Arrow', '4号 · 箭头', 0.44, Matrix(((0, -1, 0), (0, 0, 1), (1, 0, 0)))),
]

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.device = 'CPU'
scene.cycles.samples = 1
scene.render.bake.use_clear = True
scene.render.bake.margin = 8
scene.render.bake.use_selected_to_active = False
scene.view_settings.view_transform = 'Standard'
scene.render.image_settings.file_format = 'PNG'


def make_bake_material(source, image):
    """Bake the authored color graph, without illumination or transmission."""
    material = source.copy() if source else bpy.data.materials.new('Fallback')
    material.use_nodes = True
    nodes = material.node_tree.nodes
    links = material.node_tree.links
    principled = next((n for n in nodes if n.type == 'BSDF_PRINCIPLED'), None)
    color = (0.7, 0.7, 0.7, 1.0)
    roughness, metallic, alpha = 0.5, 0.0, 1.0
    emission = nodes.new('ShaderNodeEmission')
    if principled:
        base = principled.inputs['Base Color']
        color = tuple(base.default_value)
        if base.is_linked:
            links.new(base.links[0].from_socket, emission.inputs['Color'])
        else:
            emission.inputs['Color'].default_value = color
        roughness = float(principled.inputs['Roughness'].default_value)
        metallic = float(principled.inputs['Metallic'].default_value)
        alpha = float(principled.inputs['Alpha'].default_value)
    else:
        emission.inputs['Color'].default_value = color
    output = next((n for n in nodes if n.type == 'OUTPUT_MATERIAL' and n.is_active_output), None)
    if output is None:
        output = nodes.new('ShaderNodeOutputMaterial')
    links.new(emission.outputs[0], output.inputs['Surface'])
    for node in nodes:
        node.select = False
    target = nodes.new('ShaderNodeTexImage')
    target.image = image
    target.select = True
    nodes.active = target
    return material, roughness, metallic, alpha


arguments = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
only = arguments[arguments.index('--only') + 1] if '--only' in arguments else None
manifest_path = os.path.join(OUT, 'throwables.json')
existing = {}
if only:
    with open(manifest_path, encoding='utf-8') as stream:
        existing = {item['key']: item for item in json.load(stream)['items']}
    if only not in [spec[1] for spec in SPECS]:
        raise ValueError('Unknown export key: ' + only)
depsgraph = bpy.context.evaluated_depsgraph_get()
exported = {}
for collection_name, key, label, size, basis in SPECS:
    if only and key != only:
        continue
    mesh_sources = [bpy.data.objects[collection_name[1:]]] if collection_name.startswith('@') else \
        [o for o in bpy.data.collections[collection_name].all_objects if o.type == 'MESH']
    records = []
    working = []
    for index, source in enumerate(mesh_sources):
        if source.type != 'MESH':
            raise ValueError('Export source is not a mesh: ' + source.name)
        evaluated = source.evaluated_get(depsgraph)
        mesh = bpy.data.meshes.new_from_object(evaluated, preserve_all_data_layers=True, depsgraph=depsgraph)
        obj = bpy.data.objects.new(key + '_' + str(index), mesh)
        scene.collection.objects.link(obj)
        obj.matrix_world = source.matrix_world.copy()
        bpy.ops.object.select_all(action='DESELECT')
        obj.select_set(True)
        bpy.context.view_layer.objects.active = obj
        mesh.calc_loop_triangles()
        if len(mesh.loop_triangles) > 24000:
            decimate = obj.modifiers.new('Mobile mesh budget', 'DECIMATE')
            decimate.ratio = 24000.0 / len(mesh.loop_triangles)
            bpy.ops.object.modifier_apply(modifier=decimate.name)
            mesh = obj.data

        # Keep authored UVs available to source textures while baking into a new atlas.
        old_uv = mesh.uv_layers.active
        if old_uv:
            old_uv.active_render = True
        bake_uv = mesh.uv_layers.new(name='UnityBakeUV')
        mesh.uv_layers.active = bake_uv
        bpy.ops.object.mode_set(mode='EDIT')
        bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.uv.smart_project(angle_limit=math.radians(66), island_margin=0.025)
        bpy.ops.object.mode_set(mode='OBJECT')

        image_name = key + '_' + str(index) + '_BaseColor'
        image = bpy.data.images.new(image_name, width=512, height=512, alpha=False)
        image.colorspace_settings.name = 'sRGB'
        sources = list(mesh.materials) or [None]
        mesh.materials.clear()
        roughness, metallic, alpha = 0.5, 0.0, 1.0
        for material in sources:
            copy, roughness, metallic, alpha = make_bake_material(material, image)
            mesh.materials.append(copy)
        bpy.ops.object.bake(type='EMIT')
        image.filepath_raw = os.path.join(OUT, image_name + '.png')
        image.file_format = 'PNG'
        image.save()
        records.append({'objectName': obj.name, 'texture': image_name + '.png',
                        'smoothness': 1.0 - roughness, 'metallic': metallic,
                        'alpha': max(0.85, alpha) if key == 'StickyHand' else
                                 0.8 if key == 'JellyCube' else 1.0})
        working.append(obj)

    points = [basis @ (obj.matrix_world @ vertex.co) for obj in working for vertex in obj.data.vertices]
    lower = Vector(tuple(min(v[i] for v in points) for i in range(3)))
    upper = Vector(tuple(max(v[i] for v in points) for i in range(3)))
    center = (lower + upper) * 0.5
    span = max(upper - lower)
    file_path = os.path.join(OUT, key + '.obj')
    vertex_offset = 0
    with open(file_path, 'w', encoding='utf-8', newline='\n') as stream:
        stream.write('# Evaluated, centered Unity-coordinate mesh from ' + collection_name + '\n')
        for obj in working:
            mesh = obj.data
            # Baking/mesh operators can invalidate RNA layer wrappers. Reacquire
            # the layer and copy numeric values before iterating the export.
            uv_values = [tuple(item.uv) for item in mesh.uv_layers['UnityBakeUV'].data]
            vertex_normals = [vertex.normal.copy() for vertex in mesh.vertices]
            mesh.calc_loop_triangles()
            transform = basis @ obj.matrix_world.to_3x3()
            normal_transform = transform.inverted().transposed()
            positions, normals, uvs, faces = [], [], [], []
            unique = {}
            for triangle in mesh.loop_triangles:
                indices = []
                for loop_index in triangle.loops:
                    loop = mesh.loops[loop_index]
                    position = (basis @ (obj.matrix_world @ mesh.vertices[loop.vertex_index].co) - center) / span
                    local_normal = vertex_normals[loop.vertex_index] if mesh.polygons[triangle.polygon_index].use_smooth else triangle.normal
                    normal = (normal_transform @ local_normal).normalized()
                    texcoord = uv_values[loop_index]
                    signature = tuple(round(x, 7) for x in (*position, *normal, *texcoord))
                    if signature not in unique:
                        unique[signature] = len(positions) + 1
                        positions.append(tuple(position))
                        normals.append(tuple(normal))
                        uvs.append(tuple(texcoord))
                    indices.append(unique[signature] + vertex_offset)
                # Unity/OBJ importer changes handedness; emit the source's right-handed winding.
                # Our authored coordinate basis is reflected, so reverse each triangle here.
                if transform.determinant() < 0:
                    indices.reverse()
                faces.append(indices)
            stream.write('o ' + obj.name + '\n')
            for point in positions:
                stream.write('v %.8f %.8f %.8f\n' % point)
            for texcoord in uvs:
                stream.write('vt %.8f %.8f\n' % texcoord)
            for normal in normals:
                stream.write('vn %.8f %.8f %.8f\n' % normal)
            for face in faces:
                stream.write('f ' + ' '.join('%d/%d/%d' % (i, i, i) for i in face) + '\n')
            # Unity's OBJ importer can merge differently named objects. Also
            # export each mesh separately so multi-material signs remain exact.
            with open(os.path.join(OUT, obj.name + '.obj'), 'w', encoding='utf-8', newline='\n') as part:
                part.write('o ' + obj.name + '\n')
                for point in positions:
                    part.write('v %.8f %.8f %.8f\n' % point)
                for texcoord in uvs:
                    part.write('vt %.8f %.8f\n' % texcoord)
                for normal in normals:
                    part.write('vn %.8f %.8f %.8f\n' % normal)
                for face in faces:
                    part.write('f ' + ' '.join('%d/%d/%d' % (i - vertex_offset, i - vertex_offset, i - vertex_offset) for i in face) + '\n')
            vertex_offset += len(positions)

    exported[key] = {'key': key, 'displayName': label, 'sourceCollection': collection_name,
                     'longestSide': size, 'objects': records}
    print('[ThrowablesExport] ' + key + ': ' + str(len(working)) + ' meshes, ' + str(vertex_offset) + ' export vertices', flush=True)
    for obj in working:
        bpy.data.objects.remove(obj, do_unlink=True)

manifest = {'items': [exported.get(spec[1], existing.get(spec[1])) for spec in SPECS]}
if any(item is None for item in manifest['items']):
    raise ValueError('Missing model slots; run a complete export first.')
with open(manifest_path, 'w', encoding='utf-8') as stream:
    json.dump(manifest, stream, ensure_ascii=False, indent=2)
print('[ThrowablesExport] COMPLETE: ' + OUT, flush=True)
