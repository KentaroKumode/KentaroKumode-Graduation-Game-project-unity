bl_info = {
    "name": "Vertex Group L/R Swap",
    "author": "Kentaro Kumode",
    "version": (1, 0, 0),
    "blender": (5, 1, 0),
    "location": "View3D > Sidebar > Bone Rename",
    "description": "選択メッシュの頂点グループ名のL/Rサフィックスを一括スワップする",
    "category": "Rigging",
}

import bpy
from bpy.props import EnumProperty, BoolProperty
from bpy.types import Operator, Panel, PropertyGroup


# 認識する左右ペア（左, 右）。区切り文字違いと大文字小文字を網羅。
_LR_PAIRS = (
    ("_L", "_R"),
    ("_l", "_r"),
    (".L", ".R"),
    (".l", ".r"),
    ("-L", "-R"),
    ("-l", "-r"),
)

_PREFIX_PAIRS = (
    ("L_", "R_"),
    ("l_", "r_"),
    ("L.", "R."),
    ("l.", "r."),
)


def _swap_name(name, use_suffix, use_prefix):
    if use_suffix:
        for l, r in _LR_PAIRS:
            if name.endswith(l):
                return name[: -len(l)] + r
            if name.endswith(r):
                return name[: -len(r)] + l
    if use_prefix:
        for l, r in _PREFIX_PAIRS:
            if name.startswith(l):
                return r + name[len(l):]
            if name.startswith(r):
                return l + name[len(r):]
    return None


class VGroupLRSwapProps(PropertyGroup):
    target: EnumProperty(
        name="対象",
        description="リネーム対象の頂点グループ",
        items=[
            ('SELECTED', "選択中のみ", "アクティブな頂点グループのみリネーム"),
            ('MATCHED', "L/R該当のみ", "L/Rサフィックスを持つ頂点グループのみ"),
            ('ALL', "全て", "全頂点グループを対象に判定"),
        ],
        default='MATCHED',
    )
    use_suffix: BoolProperty(
        name="末尾を判定",
        description="_L/_R, .L/.R などの末尾サフィックスをスワップ",
        default=True,
    )
    use_prefix: BoolProperty(
        name="先頭を判定",
        description="L_/R_, L./R. などの先頭プレフィクスをスワップ",
        default=False,
    )
    apply_to_selected_objects: BoolProperty(
        name="選択中の全メッシュに適用",
        description="OFFならアクティブオブジェクトのみ",
        default=True,
    )


def _gather_meshes(context, multi):
    if multi:
        return [o for o in context.selected_objects if o.type == 'MESH']
    obj = context.active_object
    if obj and obj.type == 'MESH':
        return [obj]
    return []


class VGROUP_OT_lr_swap(Operator):
    bl_idname = "object.vgroup_lr_swap"
    bl_label = "頂点グループL/R一括スワップ"
    bl_description = "頂点グループ名のL/Rサフィックスを一括で入れ替えます"
    bl_options = {'REGISTER', 'UNDO'}

    def execute(self, context):
        props = context.scene.vgroup_lr_swap_props

        if not (props.use_suffix or props.use_prefix):
            self.report({'WARNING'}, "末尾/先頭の少なくとも一方を有効にしてください")
            return {'CANCELLED'}

        meshes = _gather_meshes(context, props.apply_to_selected_objects)
        if not meshes:
            self.report({'WARNING'}, "メッシュオブジェクトを選択してください")
            return {'CANCELLED'}

        total_renamed = 0
        total_objects = 0
        temp_prefix = "__VGLR_TMP__"

        for obj in meshes:
            vgs = obj.vertex_groups
            if not vgs:
                continue

            if props.target == 'SELECTED':
                active = vgs.active
                candidates = [active] if active else []
            else:
                candidates = list(vgs)

            # (vg, new_name) のペアを作る
            plan = []
            for vg in candidates:
                if vg is None:
                    continue
                new_name = _swap_name(vg.name, props.use_suffix, props.use_prefix)
                if new_name is None:
                    if props.target == 'SELECTED':
                        # 明示的に選んだものなのに該当しない場合は通知
                        self.report({'INFO'}, f"{obj.name}: '{vg.name}' はL/R該当なし")
                    continue
                plan.append((vg, new_name))

            if not plan:
                continue

            # 名前衝突回避: 一旦テンポラリ名へ
            for i, (vg, _new) in enumerate(plan):
                vg.name = f"{temp_prefix}{i}"

            for (vg, new_name) in plan:
                vg.name = new_name

            total_renamed += len(plan)
            total_objects += 1

        if total_renamed == 0:
            self.report({'WARNING'}, "リネーム対象がありませんでした")
            return {'CANCELLED'}

        self.report({'INFO'}, f"{total_objects}個のメッシュ / {total_renamed}個の頂点グループをスワップしました")
        return {'FINISHED'}


class VIEW3D_PT_vgroup_lr_swap(Panel):
    bl_label = "VGroup L/R Swap"
    bl_idname = "VIEW3D_PT_vgroup_lr_swap"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = "Bone Rename"

    def draw(self, context):
        layout = self.layout
        props = context.scene.vgroup_lr_swap_props

        col = layout.column(align=True)
        col.prop(props, "target")
        col.prop(props, "use_suffix")
        col.prop(props, "use_prefix")
        col.prop(props, "apply_to_selected_objects")

        layout.separator()
        layout.operator(VGROUP_OT_lr_swap.bl_idname, icon='ARROW_LEFTRIGHT')


classes = (
    VGroupLRSwapProps,
    VGROUP_OT_lr_swap,
    VIEW3D_PT_vgroup_lr_swap,
)


def register():
    for c in classes:
        bpy.utils.register_class(c)
    bpy.types.Scene.vgroup_lr_swap_props = bpy.props.PointerProperty(type=VGroupLRSwapProps)


def unregister():
    del bpy.types.Scene.vgroup_lr_swap_props
    for c in reversed(classes):
        bpy.utils.unregister_class(c)


if __name__ == "__main__":
    register()
