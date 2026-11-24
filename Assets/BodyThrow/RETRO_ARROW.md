# 4号 · 复古红绿箭头

4号箭头采用参考图的红色箭杆、绿黑色箭头、青绿尾端及米白绑线配色，表面包含细小干墨颗粒、少量掉色和斜向尾端纹样。

本次保留原有 `Arrow_0.obj`、UV、预制体层级、模型尺寸、碰撞体、朝向以及投掷/命中逻辑。原模型的尾端是窄杆，参考图中的宽尾羽以尾端青绿纹样呼应，没有增加尾羽几何体。

## 资源

- 新贴图：`Assets/Resources/Throwables/Materials/ArrowRetro_BaseColor.png`
- 使用现有材质：`Assets/Resources/Throwables/Materials/Arrow_0.mat`
- 哑光着色器：`Assets/Shaders/RetroArrow.shader`，宽范围柔光保留圆柱明暗，避免细红杆在 AR 中过暗。
- 轮盘缩略图：`Assets/Resources/Throwables/Previews/Arrow.png`
- Unity 侧视渲染：`Assets/BodyThrow/ArtPreviews/RetroArrowSide.png`

## 重建

1. 执行 `python Tools/generate_retro_arrow_art.py`（Pillow、NumPy）。脚本读取原 OBJ 的顶点和 UV，将配色烘焙至新贴图，不修改模型文件。
2. Unity 菜单 **Tools → Earth OL → Apply retro arrow style**，应用材质并生成缩略图和侧视图，同时检查渲染配色及现有箭头投掷、命中贴附与人物跟随。

亮度和整体颜色可在 `Arrow_0.mat` 的 **Tint** 中调整；区域配色由贴图决定。
