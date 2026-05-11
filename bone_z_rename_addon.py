bl_info = {
    "name": "Bone Z Rename",
    "author": "Kentaro Kumode",
    "version": (1, 0, 0),
    "blender": (5, 1, 0),
    "location": "View3D > Sidebar > Bone Rename",
    "description": "選択中のボーンをZ座標基準（上から）で連番リネームする",
    "category": "Rigging",
}

import bpy
from bpy.props import StringProperty, EnumProperty, IntProperty, BoolProperty
from bpy.types import Operator, Panel, PropertyGroup


class BoneZRenameProps(PropertyGroup):
    base_name: StringProperty(
        name="名前",
        description="リネーム時のベース名",
        default="Bone",
    )
    side: EnumProperty(
        name="左右",
        description="末尾に付けるサフィックス",
        items=[
            ('R', "右 (R)", "右側 _R"),
            ('L', "左 (L)", "左側 _L"),
            ('NONE', "なし", "サフィックスを付けない"),
        ],
        default='R',
    )
    start_index: IntProperty(
        name="開始番号",
        description="最も上のボーンに割り当てる番号",
        default=0,
        min=0,
    )
    descending: BoolProperty(
        name="上から昇順",
        description="ON: 上(高Z)が小さい番号 / OFF: 下(低Z)が小さい番号",
        default=True,
    )


def _gather_selected_bones(context):
    obj = context.active_object
    if obj is None or obj.type != 'ARMATURE':
        return None, None, "アーマチュアを選択してください"

    mode = obj.mode
    items = []

    if mode == 'EDIT':
        for eb in obj.data.edit_bones:
            if eb.select or eb.select_head or eb.select_tail:
                items.append((eb, eb.head.z))
    elif mode == 'POSE':
        for pb in obj.pose.bones:
            if pb.bone.select:
                items.append((pb.bone, pb.bone.head_local.z))
    else:
        return None, None, "Edit ModeかPose Modeで実行してください"

    if not items:
        return None, None, "選択中のボーンがありません"

    return mode, items, None


class BONE_OT_z_rename(Operator):
    bl_idname = "bone.z_rename"
    bl_label = "Z座標で連番リネーム"
    bl_description = "選択中のボーンをZ座標基準で並べ、連番でリネームします"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        props = context.scene.bone_z_rename_props
        mode, items, err = _gather_selected_bones(context)
        if err:
            self.report({'WARNING'}, err)
            return {'CANCELLED'}

        items.sort(key=lambda t: t[1], reverse=props.descending)

        suffix = "" if props.side == 'NONE' else f"_{props.side}"
        base = props.base_name.strip() or "Bone"

        # 名前衝突を避けるため、一旦テンポラリ名にしてからリネーム
        temp_prefix = "__BZR_TMP__"
        for i, (bone, _z) in enumerate(items):
            bone.name = f"{temp_prefix}{i}"

        for i, (bone, _z) in enumerate(items):
            idx = props.start_index + i
            bone.name = f"{base}_{idx}{suffix}"

        self.report({'INFO'}, f"{len(items)}本のボーンをリネームしました")
        return {'FINISHED'}


class VIEW3D_PT_bone_z_rename(Panel):
    bl_label = "Bone Z Rename"
    bl_idname = "VIEW3D_PT_bone_z_rename"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "Bone Rename"

    def draw(self, context):
        layout = self.layout
        props = context.scene.bone_z_rename_props

        col = layout.column(align=True)
        col.prop(props, "base_name")
        col.prop(props, "side")
        col.prop(props, "start_index")
        col.prop(props, "descending")

        layout.separator()
        layout.operator(BONE_OT_z_rename.bl_idname, icon='SORTSIZE')


classes = (
    BoneZRenameProps,
    BONE_OT_z_rename,
    VIEW3D_PT_bone_z_rename,
)


def register():
    for c in classes:
        bpy.utils.register_class(c)
    bpy.types.Scene.bone_z_rename_props = bpy.props.PointerProperty(type=BoneZRenameProps)


def unregister():
    del bpy.types.Scene.bone_z_rename_props
    for c in reversed(classes):
        bpy.utils.unregister_class(c)


if __name__ == "__main__":
    register()
