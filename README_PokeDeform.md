# 戳一戳：让被识别到的物体形变

> 工程：`My project`　Unity **2022.3.62f3c1**　Vuforia Engine **11.4.4**　目标平台 Android（ARM64 / IL2CPP）

---

## 1. 效果

手指按到识别图上的虚拟物体时：

1. **局部凹陷** —— 表面立刻被压出一个平滑的坑（顶点级形变 + 法线跟着弯曲，有真实的凹陷光照）；
2. **按住不放** —— 坑停在那儿不弹回；手指在物体表面滑动，坑跟着手指走；
3. **抬手** —— 弹簧回弹，并且**过冲**：先凹进去、回弹时向外鼓一下、再归位，就是果冻感；
4. **整体挤压** —— 同时沿受力方向短暂压扁、垂直方向膨胀（体积守恒），看起来更像软体；
5. 支持多指：最多同时 4 个坑。

---

## 2. 这个工程里"识别"是怎么来的（重要）

工程原有的两个 ImageTarget 是 **Instant Image Target（运行时图片目标）**，不是数据集目标：

- `ImageTarget` 物体的 target 名是 `3_scaled`，`ImageTarget (1)` 是 `6_scaled`；
- 它们的 `Image Target Type = Instant`，直接用 `mRuntimeTexture` 指向的图片在运行时生成目标；
- 图片在 `Assets/Editor/Vuforia/ImageTargetTextures/cios/3_scaled.jpg`、`6_scaled.jpg`
  （导入设置 `Read/Write = 1`，符合 Instant 目标的要求）。

所以：**`Assets/StreamingAssets/Vuforia/cios.dat` / `cios.xml` 跟这两个目标没有关系**，里面只声明了 1 个 target 也不影响。
想测试的话，直接打印那两个 jpg 就行。

---

## 3. 新增的文件

| 文件 | 作用 |
| --- | --- |
| [PokeInput.cs](Assets/Scripts/PokeDeform/PokeInput.cs) | 把触摸 / 鼠标变成射线，只命中"正在被识别"的物体，转交给 `IPokeTarget` |
| [PokeDeformer.cs](Assets/Scripts/PokeDeform/PokeDeformer.cs) | 形变主体：维护最多 4 个戳点、跑弹簧、写 `MaterialPropertyBlock`、整体挤压 |
| [IPokeTarget.cs](Assets/Scripts/PokeDeform/IPokeTarget.cs) | 可被戳的物体的接口（想换成别家的形变效果也能接） |
| [SoftMesh.cs](Assets/Scripts/PokeDeform/SoftMesh.cs) | 运行时生成高密度网格（圆角方块 / 细分球），顶点太少戳不出坑 |
| [PokeDeformBootstrap.cs](Assets/Scripts/PokeDeform/PokeDeformBootstrap.cs) | **运行时自愈**：启动时补齐缺失的组件 / 材质 / 高密度网格 |
| [SceneDiagnostics.cs](Assets/Scripts/PokeDeform/SceneDiagnostics.cs) | 启动时打印场景与组件状态，排查用（可删） |
| [PokeDeform.shader](Assets/Shaders/PokeDeform.shader) | `EarthOL/PokeDeform`：按 4 个戳点做顶点位移 + 法线修正 |
| [PokeDeformSetup.cs](Assets/Editor/PokeDeformSetup.cs) | 一键配置 / 校验 / 生成材质的编辑器工具 |
| [PokeDeformSelfTest.cs](Assets/Editor/PokeDeformSelfTest.cs) | 自检：网格顶点/包围盒/法线/三角面 + 弹簧是否会"过冲且收敛" |
| [AndroidBuild.cs](Assets/Editor/AndroidBuild.cs) | 一键打 APK（强制 clean build，避免打进旧场景） |

材质放在 `Assets/Resources/PokeJelly_Orange.mat`、`PokeJelly_Teal.mat`
—— 放在 `Resources` 下是为了**保证一定被打进包**，并且运行时能用 `Resources.Load` 兜底。

场景 `Assets/Scenes/SampleScene.unity` 里已经配置好：
`Cube` 换成圆角方块 + `PokeDeformer`（橙），`Sphere` 换成细分球 + `PokeDeformer`（青），`ARCamera` 上挂 `PokeInput`。

---

## 4. 跑起来

### 在电脑上先看效果（不用手机）

1. 打开 `Assets/Scenes/SampleScene.unity`；
2. 点 Play —— Vuforia 会用摄像头当背景，在 Game 视图里**用鼠标点击那个方块 / 球**就能看到形变；
3. 物体在场景里位于原点附近，Camera 的 Clear Flags 是 Solid Color，找不到可以用 Scene 视图看清位置。

> 想无视摄像头直接触发：运行时在 `PokeDeformer` 组件上右键 → **测试：戳一下（Play 模式）**。

### 在手机上

1. `File → Build Settings → Android → Switch Platform`（工程已经是 Android）；
2. 手机连上电脑，`Build And Run`；
3. 打印 `Assets/Editor/Vuforia/ImageTargetTextures/cios/3_scaled.jpg`（或 `6_scaled.jpg`）平铺在桌上，
   手机对准它 —— 出现方块 / 球以后戳它。

---

## 5. 参数怎么调

选中 `Cube` 或 `Sphere`，在 `Poke Deformer` 组件上：

| 参数 | 含义 | 调大的效果 |
| --- | --- | --- |
| `Dent Radius Ratio` | 凹陷半径（相对物体尺寸） | 坑更大、更"软" |
| `Max Depth Ratio` | 最大凹陷深度（相对物体尺寸） | 戳得更深 |
| `Max Dents` | 同时显示几个坑（≤4） | 多指可同时戳更多地方 |
| `Recovery Frequency` | 回弹频率（Hz） | 回弹更快、更"弹" |
| `Damping Ratio` | 阻尼比 | **小于 1 才过冲**；越小越抖（0.2~0.4 最像果冻） |
| `Squash Amount` | 整体挤压强度 | 压扁更明显 |
| `Soft Mesh` | 网格替换方式 | `RoundedCube` / `Sphere`；`KeepOriginal` 保留原网格 |
| `Soft Mesh Level` | 细分等级 0~4 | 更细腻（也更多顶点） |
| `Rounding` | 圆角程度 | 0 = 硬边，0.3 左右像软方块 |
| `Vibrate On Poke` | 戳中时震动 | 需要在 Player Settings 里给 Android 勾 `VIBRATE` 权限 |

想改颜色：直接改材质 `Assets/Materials/PokeJelly_*.mat` 的 `颜色`。

---

## 6. 实现要点（想继续扩展时的关键点）

1. **"只戳被识别到的物体"是免费的**：Vuforia 的 `DefaultObserverEventHandler` 在丢失跟踪时会把目标下的
   `Renderer` / `Collider` 全部 `enabled = false`。射线打不到被禁用的 Collider，所以**不需要额外判断跟踪状态**。
2. **为什么用 `LateUpdate`**：Vuforia 在那之前已经把 ImageTarget 的位姿写好，形变回弹不会滞后一帧。
3. **法线修正**：位移是 `沿顶点法线 -n * amp * falloff`，法线用衰减场的**切向梯度**掰向坑内侧；
   不修正的话坑看起来是平的。
4. **网格密度决定效果**：Unity 自带 `Cube` 只有 24 个顶点，面上没有顶点就压不出坑 ——
   这就是 `SoftMesh` 存在的原因。换成圆角方块后包围盒仍是 1 单位、内接半径 0.5，原来的 `BoxCollider` 依然贴合。
5. **Shader 必须被材质引用才会打进包**（Unity 会剔除没人用的着色器），所以工具是生成 `.mat` 资源，
   而不是只靠 `Shader.Find`。组件里那条 `Shader.Find` 只是运行时兜底。
6. **两个戳偏一点也能戳中**：`PokeInput.Forgiveness Radius`（默认 8 mm）在小物体上做了一次小球扫掠，
   手机上手不用点得特别准。

---

## 7. 已知限制

- 形变是**按顶点**算的：顶点越多越细腻；等级调到 4 时圆角方块每面 14 段。
- 一个物体最多同时 4 个凹坑（shader 里写死 4 组参数，要更多得改 shader）。
- 整体挤压会改 `localScale`，所以物体原本的儿童缩放比例会被当作基准（`Awake` 时记录），
  动画 / 其他脚本如果也在改它的 scale 会打架。
- 我没有改动你的识别数据集、License、Build Settings 和包列表。

---

## 8. 想给别的物体加这个效果

选中物体 → 菜单 `Tools → Earth OL → ① 配置当前场景（戳形变）`，
或者手动：加 `Poke Deformer` 组件 → 把材质换成 `EarthOL/PokeDeform` 的材质 →
`Soft Mesh` 选 `RoundedCube` / `Sphere`；物体上必须有 `Collider`（射线要能打到）。

配置完可以点 `Tools → Earth OL → ② 校验配置` 自查一遍（顶点数、材质、相机上的 `PokeInput` 都会检查），
`④ 自检（网格 + 弹簧）` 会验证网格生成和弹簧参数是否合理。

> 凹陷大小是按 **Collider 的尺寸**算的，所以 Collider 要和外观贴合；
> 换网格时（圆角方块的包围盒仍是 1 单位）原来的 `BoxCollider` / `SphereCollider` 可以直接沿用。

---

## 9. 识别图卡片本体形变（PokeWarpDeformer）

除了"在识别图上摆的方块/球"可以戳出凹陷，还可以让**被识别的卡片本身**形变：

- 组件直接挂在 `ImageTarget` 这个物体上（`MeshFilter / MeshRenderer / MeshCollider / PokeWarpDeformer`），
  **不新建任何 GameObject**；
- 运行时生成 16×28 的高密度平面网格，贴图取 ImageTarget 的 `mRuntimeTexture`（就是那张识别图画面）；
- 戳卡片 → 面内局部拉伸（画面被拉开/放大），回弹时变负 = 局部压缩，同时轻微向外鼓起；
- 卡片只在目标被识别时显示（跟随 Vuforia 对 Collider 的开关），目标丢失就一起隐藏。

调参：选中 `ImageTarget` 物体上的 `Poke Warp Deformer`
`Stretch Amount`（画面拉伸幅度）、`Bulge Amount`（鼓起高度）、`Damping Ratio`（回弹抖动）、`Grid X/Y`（网格密度）。

> 注意：卡片网格是运行时生成的，所以编辑期 Scene 视图里看不到它，运行后才出现。
> 另外卡片本体不要再挂 `PokeDeformer`，两个组件会抢同一个触摸事件。


## 10. 打 APK

菜单 `Tools → Earth OL → ⑤ 打 Android APK（真机试用）`，产物 `Builds/EarthOL-PokeDeform.apk`。
命令行等价：

```
Unity.exe -batchmode -quit -projectPath "<工程路径>" -buildTarget Android ^
  -executeMethod EarthOL.EditorTools.AndroidBuild.BuildApk -logFile "<工程路径>\Logs\build.log"
```

### ⚠ 踩过的坑：增量构建会打进旧场景

改过场景后打包，如果只是普通增量构建，Unity 会复用旧的场景产物 —— 打出来的包里是**改动前**的场景
（表现为：物体是默认白色硬边方块、戳不动，但编辑器里一切正常）。

所以 `AndroidBuild` 里固定加了 `BuildOptions.CleanBuildCache`，并在构建前清 `Temp/StagingArea`、
`Library/BuildPlayerData`。另外构建前会断言"场景里必须有 PokeDeformer / PokeInput"，缺失直接报错，
不会再静默打出一个没有功能的包。

---

## 11. 真机验证记录（vivo V2603A / Android 16 / arm64-v8a）

| 项目 | 实测证据 |
| --- | --- |
| 安装 | `pm install` Success，包名 `com.DefaultCompany.Myproject` |
| Vuforia 初始化 | `Starting Vuforia Engine instance...SUCCESS` / `Vuforia Started`（license 正常） |
| Instant 融合图 | `Creating ImageTarget from Texture: 3_scaled/3_scaled`、`6_scaled` |
| 识别 | `Target status: 3_scaled TRACKED -- NORMAL` |
| 渲染 | `[Diag] Cube: mesh=PokeRoundedCube/728 mat=PokeJelly_Orange shader=EarthOL/PokeDeform` |
| 形变链路 | `[Poke] begin ... radius=0.400 amp=0.180 shaderReady=True` → `[Poke] end 开始回弹` |

打开着 `PokeDeformer.LogPokes`（在 `PokeDeformBootstrap` 里设为 `true`），每次戳都会打一条
`[Poke]` 日志；确认没问题后把那一行删掉即可关闭。


---

# 克隆后如何继续加工（必读）

## 1. 必做：导出模型权重（权重未入库）

`Assets/Resources/Models/yolov8n.onnx` **不在仓库里**（12 MB 二进制，避免拖慢拉取）。拉下来后先导出：

```powershell
pip install ultralytics
yolo export model=yolov8n.pt format=onnx opset=9 imgsz=416 simplify=True
copy yolov8n.onnx "Assets\Resources\Models\yolov8n.onnx"
```

**三个参数不能改**，否则 Barracuda 导入失败：

| 参数 | 值 | 原因 |
| --- | --- | --- |
| `opset` | **9** | YOLOv8 默认 opset 12 的算子 Barracuda 2.0.0 不支持 |
| `simplify` | **True** | 简化后的图才能被 Barracuda 导入 |
| `imgsz` | **416** | 必须与 `YoloPersonDetector.InputSize` 一致，改尺寸要两边同步 |

`com.unity.barracuda` 必须是 **2.0.0**（3.0 已移除 ONNX 导入）。

## 2. 必做：换成自己的 Vuforia License Key

`Assets/Resources/VuforiaConfiguration.asset` 里的 key 绑定包名 `com.DefaultCompany.Myproject`，
别人用会初始化失败。到 [developer.vuforia.com](https://developer.vuforia.com) 免费申请，
填相同包名即可，然后替换 `vuforiaLicenseKey`。

## 3. 环境要求

- Unity **2022.3.62f3c1**
- Android：**IL2CPP / ARM64 / minSdk 29**
- Active Input Handling：**仅 Input System（New）** —— 旧的 `Input.touchCount` 会抛异常
- 首次打开需联网（解析 Vuforia scoped registry + Barracuda）

## 4. 已知问题 / 待办

- 选中后的代理体是**隐形投影面**（静止时与背景一致），锁定后缺少可见边界提示；
- 锁定目标后没有"取消选中/换目标"入口；
- 人检测 416 在真机约 **300 ms/帧**，扫描间隔 1.5 s，FPS 会周期性下降；
- 物体不做自动识别，由手指点选指定。
