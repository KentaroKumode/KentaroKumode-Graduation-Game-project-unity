"""Blenderから直接実行するCollection High / Low Builderのスモークテスト。"""

import sys
from pathlib import Path

import bpy


PROJECT_ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(PROJECT_ROOT))

import collection_high_low_builder as addon


addon.register()

source = bpy.data.collections.new("TestSource")
bpy.context.scene.collection.children.link(source)
child = bpy.data.collections.new("TestChild")
source.children.link(child)

created = []
for name in ("YYYY1135", "AAAA", "GGG", "BBBB"):
    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata([(0, 0, 0), (1, 0, 0), (0, 1, 0)], [], [(0, 1, 2)])
    obj = bpy.data.objects.new(name, mesh)
    source.objects.link(obj)
    created.append(obj)

# 親子関係もlow側の対応オブジェクトへ付け替わることを確認する。
created[2].parent = created[1]

props = bpy.context.scene.chlb_props
props.source_collection = source
# 既存の役割サフィックスが二重付与されないことも確認する。
props.base_name = "TEST_high_low_"
props.object_order = 'NAME'
props.include_children = True
props.copy_object_data = True

result = bpy.ops.collection.build_high_low()
assert result == {'FINISHED'}, result

high = list(source.objects)
low_collection = bpy.data.collections["TestSource_low"]
low = list(low_collection.objects)
assert low_collection.children.get("TestChild_low") is not None

expected_high = [f"TEST_{index}_high" for index in range(1, 5)]
expected_low = [f"TEST_{index}_low" for index in range(1, 5)]
assert sorted(obj.name for obj in high) == expected_high
assert sorted(obj.name for obj in low) == expected_low

for index in range(1, 5):
    high_obj = bpy.data.objects[f"TEST_{index}_high"]
    low_obj = bpy.data.objects[f"TEST_{index}_low"]
    assert high_obj.type == low_obj.type
    assert high_obj.data is not low_obj.data

assert bpy.data.objects["TEST_3_low"].parent == bpy.data.objects["TEST_1_low"]
assert props.last_result.startswith("照合OK: 4組"), props.last_result

print("CHLB_SMOKE_TEST_OK")
