# Builds the rigged bow from the Tripo FBX: replaces the string with a two-segment one,
# adds the "Drawn" shape key (limbs bend, nocking point pulled back) and the Arrow_Rest /
# Draw_Full empties, then saves a .blend and exports an FBX for Unity.
# Usage: blender --background --python rig_bow.py -- <in.fbx> <out.blend> <out.fbx>
import bpy, bmesh, sys, math
from mathutils import Vector, Matrix

args = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT_BLEND, OUT_FBX = args

# --- Measurements of the Tripo model, in its imported space (Z up, string at +X, arrow towards -X) ---
GRIP_CENTRE = Vector((-0.055, 0.0, 0.5))   # middle of the leather wrap; becomes the origin
ARROW_Z = 0.57                              # just above the wrap: where the arrow lies on the riser
STRING_X = 0.105
STRING_TOP_Z = 0.918
STRING_BOTTOM_Z = 0.078
STRING_RADIUS = 0.0025
STRING_SIDES = 6
# --- Draw ---
DRAW_LENGTH = 0.5                           # nocking point pulled this far back at full draw
LIMB_ROOT = 0.12                            # limbs start bending this far above/below the grip centre
LIMB_PIVOT_X = -0.11                        # limb root on the riser's front edge
LIMB_BEND_DEGREES = 18.0                    # rotation of each limb tip at full draw


def limb_offset(p):
    """Where point p moves at full draw: rotated about its limb root (side axis), angle easing in along the limb."""
    dz = p.z - GRIP_CENTRE.z
    span = 0.5 - LIMB_ROOT
    t = min(max((abs(dz) - LIMB_ROOT) / span, 0.0), 1.0)
    if t <= 0.0:
        return p.copy()
    side = 1.0 if dz > 0 else -1.0
    pivot = Vector((LIMB_PIVOT_X, p.y, GRIP_CENTRE.z + side * LIMB_ROOT))
    # Upper limb rotates clockwise seen from -Y (tip goes back to +X and down); lower mirrors.
    angle = math.radians(LIMB_BEND_DEGREES) * (t ** 1.5) * side
    rot = Matrix.Rotation(angle, 3, 'Y')
    return pivot + rot @ (p - pivot)


bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
bow = next(o for o in bpy.context.scene.objects if o.type == 'MESH')
bow.name = "Bow"
bow.data.name = "Bow"
bpy.context.view_layer.objects.active = bow

# --- Replace the string ---
bm = bmesh.new()
bm.from_mesh(bow.data)
uv_layer = bm.loops.layers.uv.active
string_verts = [v for v in bm.verts
                if abs(v.co.x - STRING_X) < 0.006 and STRING_BOTTOM_Z - 0.002 < v.co.z < STRING_TOP_Z + 0.002
                and abs(v.co.y) < 0.004]
# Keep only whole islands lying entirely in the string's thin box.
string_set = set(string_verts)
island_ok = set()
for v in string_verts:
    if v in island_ok:
        continue
    stack, isl, ok = [v], {v}, True
    while stack:
        c = stack.pop()
        for e in c.link_edges:
            w = e.other_vert(c)
            if w not in isl:
                isl.add(w)
                stack.append(w)
                if w not in string_set:
                    ok = False
    if ok:
        island_ok |= isl
print("string verts removed:", len(island_ok))
uv_sum, uv_n = Vector((0.0, 0.0)), 0
for v in island_ok:
    for loop in v.link_loops:
        uv_sum += loop[uv_layer].uv
        uv_n += 1
string_uv = uv_sum / max(uv_n, 1)
mat_index = next(iter(island_ok)).link_faces[0].material_index
bmesh.ops.delete(bm, geom=list(island_ok), context='VERTS')

rings = []
for z in (STRING_BOTTOM_Z, ARROW_Z, STRING_TOP_Z):
    ring = []
    for i in range(STRING_SIDES):
        a = 2 * math.pi * i / STRING_SIDES
        ring.append(bm.verts.new((STRING_X + STRING_RADIUS * math.cos(a), STRING_RADIUS * math.sin(a), z)))
    rings.append(ring)
new_faces = []
for r0, r1 in zip(rings, rings[1:]):
    for i in range(STRING_SIDES):
        j = (i + 1) % STRING_SIDES
        f = bm.faces.new((r0[i], r0[j], r1[j], r1[i]))
        f.material_index = mat_index
        f.smooth = True
        new_faces.append(f)
bmesh.ops.recalc_face_normals(bm, faces=new_faces)
for f in new_faces:
    for loop in f.loops:
        loop[uv_layer].uv = string_uv
bm.verts.index_update()
ring_index = [[v.index for v in r] for r in rings]
bm.to_mesh(bow.data)
bm.free()

# --- Shape keys ---
bow.shape_key_add(name="Basis", from_mix=False)
drawn = bow.shape_key_add(name="Drawn", from_mix=False)
ring_ids = {i for r in ring_index for i in r}
for v in bow.data.vertices:
    if v.index not in ring_ids:
        drawn.data[v.index].co = limb_offset(v.co)
string_end = {}
for (r, z) in zip(ring_index, (STRING_BOTTOM_Z, ARROW_Z, STRING_TOP_Z)):
    centre = Vector((STRING_X, 0.0, z))
    if z == ARROW_Z:
        moved = centre + Vector((DRAW_LENGTH, 0.0, 0.0))
    else:
        moved = limb_offset(centre)
    for i in r:
        drawn.data[i].co = bow.data.vertices[i].co + (moved - centre)
    string_end[z] = (centre, moved)
print("tip move top:", tuple(round(c, 3) for c in (string_end[STRING_TOP_Z][1] - string_end[STRING_TOP_Z][0])))

# --- Empties (in the same imported space, before re-orienting) ---
def add_empty(name, loc):
    e = bpy.data.objects.new(name, None)
    e.empty_display_type = 'PLAIN_AXES'
    e.empty_display_size = 0.05
    e.location = loc
    bpy.context.scene.collection.objects.link(e)
    return e

arrow_rest = add_empty("Arrow_Rest", Vector((GRIP_CENTRE.x, 0.0, ARROW_Z)))
draw_full = add_empty("Draw_Full", Vector((STRING_X + DRAW_LENGTH, 0.0, ARROW_Z)))
string_rest = add_empty("String_Rest", Vector((STRING_X, 0.0, ARROW_Z)))
empties = (arrow_rest, draw_full, string_rest)

# --- Origin at the grip, then turn so the arrow points along Blender -Y (Unity +Z) ---
shift = Matrix.Translation(-GRIP_CENTRE)
turn = Matrix.Rotation(math.radians(90), 4, 'Z')   # -X -> -Y, string +X -> +Y
xf = turn @ shift
bow.data.transform(xf)
for kb in bow.data.shape_keys.key_blocks:
    for d in kb.data:
        d.co = xf @ d.co
for e in empties:
    e.location = xf @ e.location
bow.matrix_world = Matrix.Identity(4)
bow.data.update()
for e in empties:
    e.parent = bow
    e.matrix_parent_inverse = Matrix.Identity(4)

for o in (bow,) + empties:
    print(o.name, tuple(round(c, 4) for c in o.location))

bpy.ops.wm.save_as_mainfile(filepath=OUT_BLEND)
bpy.ops.export_scene.fbx(
    filepath=OUT_FBX, use_selection=False, object_types={'MESH', 'EMPTY'},
    apply_scale_options='FBX_SCALE_ALL', axis_forward='-Z', axis_up='Y', bake_space_transform=True,
    use_mesh_modifiers=True, mesh_smooth_type='FACE', use_tspace=False,
    bake_anim=False, add_leaf_bones=False, path_mode='AUTO', embed_textures=False)
print("exported", OUT_FBX)
