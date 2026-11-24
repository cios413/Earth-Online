# 梦境手雷

已加入 `Assets/Resources/Throwables/Catalog.asset`，选择名为 **6号 · 梦境手雷**。
在自由模式或战斗模式开始人物识别，上拉底部轮盘，左右滑动选中手雷，点击人物即可投掷。

## 表现与时序

- 飞行沿用现有投掷轨迹，使用透明底平面贴图，始终朝向相机，带少量平面内转动。
- 贴图按参考图重新绘制：粉色珠光球体、烘焙立体高光与柔影、心形压纹、拉环和星形吊牌。
- 命中时上报一次人物交互；约 **0.16 秒**内亮度升至粉白色，同时向外散出光晕。
- 随后隐藏手雷，按 **Burst Seconds** 设置的时长播放珠光液体喷溅，以苹果绿、青柠绿和浅薄荷绿为主，少量玫红色点缀。**命中点是画面底部的喷发中心**，各条液柱从中心周围相互错开的起点向上、向左右两侧沿三次贝塞尔曲线伸展。
- 各喷射起点到中心的距离在 **Emission Radius Range** 内分布，并随时间缓慢变化。液滴也从错开的起点飞出。中心保留透明空隙，雾和泛光在空隙边缘柔和淡出。
- 液柱根部细、头部膨大并闭合成圆润水滴，带浅绿、奶油黄绿反射、侧面高光和柔和光晕。十条液柱中八条使用绿色系，两条较小液柱使用玫红色；小液滴与星星也以绿色系为主。小液滴独立沿曲线飞出，后段液柱从根部收缩脱离并略微下落。
- 喷溅开始后 **0.46 秒**起，四角星和微光粒子分批闪现；每颗星都有独立的渐亮、闪光、淡出时序。
- 雾团与微细颗粒只落在特效覆盖区域，局部泛光围绕液柱和液滴扩散。
- 效果结束后销毁投掷物及其运行时网格。清空效果、停止识别、丢失人物时也走原有清理流程。
- 战斗模式复用目录自动注册的投掷攻击，伤害在人物命中时触发一次。

Bloom 观感由局部加色光晕与柔光光带实现，不依赖全屏后处理。柔焦、色彩溢出与薄雾也限制在爆发范围内，真实相机画面继续使用项目现有渲染流程。

## 调整

在 `Assets/Resources/Throwables/DreamGrenade.prefab` 的 `DreamGrenadeProjectile` 组件中：

| Inspector 参数 | 默认值 | 用途 |
| --- | --- | --- |
| Flash Seconds | 0.16 | 命中后变亮至隐藏的时间 |
| Burst Seconds | 3.4 | 雾光与星尘展开、停留和淡出的总时间 |
| Burst Scale | 1 | 爆发范围倍率 |
| Emission Radius Range | X = 0.32，Y = 0.64 | 各喷射起点距中心的最小、最大距离，随 Burst Scale 一起缩放 |

`DreamGlow.mat` 的 **Glow strength** 控制加色泛光强度；`DreamMist.mat` 控制薄雾亮度。两者的 **Fine film grain** 调整颗粒感。颜色与粒子数量定义在 `Runtime/DreamGrenadeProjectile.cs`。

每个爆发使用两个批量网格（雾团和辉光），复用顶点列表；同屏最多保留四枚梦境手雷，继续投掷时回收最早的一枚。

## 资源与验证

- 主图：`Assets/Resources/Throwables/Previews/DreamGrenade.png`（1024 × 1024 RGBA）。
- Unity 实际渲染序列：`Assets/BodyThrow/ArtPreviews/DreamGrenadeSequence.png`，按从左到右、从上到下为飞行外观、命中亮闪、液体初次喷出、液体伸展与星星出现、喷溅展开、液滴散开淡出；前两格为近景。
- 动态预览：`Assets/BodyThrow/ArtPreviews/DreamGrenadeSplash.gif`，由 Unity 连续渲染的 66 帧合成。
- 展开后的静帧：`Assets/BodyThrow/ArtPreviews/DreamGrenadeSplash.png`。
- **实时预览窗口**：Unity 菜单 **Tools → Earth OL → Preview dream grenade splash**，可循环播放、暂停、重播、拖动时间轴；使用与人物命中完全相同的运行时组件和着色器。
- 重绘透明贴图：`python Tools/generate_dream_grenade_art.py`，依赖 Pillow、NumPy。
- 重建预制体与材质：Unity 菜单 **Tools → Earth OL → Build dream grenade assets**。
- 验证：Unity 菜单 **Tools → Earth OL → Validate dream grenade**。
- 批处理入口：`EarthOL.EditorTools.DreamGrenadeAssets.BuildAndValidate`。
- 验证完成后合成动图：`python Tools/animate_dream_grenade_preview.py`。

验证涵盖目录引用、平面朝向、命中时序、单次战斗伤害、自动回收、取消、丢失人物、并发上限、底部起点的向上喷溅构图、星星延迟闪现，以及真实 GPU 渲染中的亮度提升、各喷溅阶段的中央空隙和特效消失。
