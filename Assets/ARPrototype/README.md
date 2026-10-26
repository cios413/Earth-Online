# AR 投掷贴附原型

## 环境与入口

- Unity **2022.3.53f1c1**，内置渲染管线。
- AR Foundation / ARCore XR Plugin / ARKit XR Plugin：**5.1.6**。
- XR Plug-in Management：**4.4.0**；Legacy Input Helpers：由包管理器解析（当前 **2.1.11**）。
- 保留 **Input Manager (Old)**（Player > Active Input Handling）；AR Foundation 会间接安装 Input System 包，但本原型不要求切换输入后端。若弹出启用新版输入后端的提示，选择 **No**。Both 也可用，不应设为仅 Input System。
- 原项目只有默认 SampleScene，没有 AR 包、XR Loader 或构建场景。新场景为 `Assets/ARPrototype/Scenes/ARThrowPrototype.unity`，已加入 Build Settings。
- 首次脚本编译完成会自动创建缺少的场景、Prefab、材质与 XR 设置。也可执行菜单 **AR Prototype > Create Missing Prototype Assets**；此操作保留已存在的原型资源。

## 直接运行

1. 打开 `ARThrowPrototype.unity`，点击 Play。
2. 在 Game 窗口点击蓝色长方体发射橙色小立方体。Editor 使用同一台 AR Camera 预览，背景为纯色，没有真实摄像头画面；手机构建启用 AR 摄像头、背景和位姿追踪。
3. 左侧红色方块是非目标遮挡物。把它移到目标前方，再点击目标，应销毁投掷物。
4. 命中后，在 Hierarchy 展开 `TargetCube`，能看到附着的 `Projectile(Clone)`；改变 **TargetCube 根节点**的位置和旋转验证跟随。
5. 测试不同表面时，可以在 Play 中旋转 TargetCube 再点击，或移动 AR Camera。不要只旋转 `TargetShape`：它只是外观与碰撞形状，外部模块应更新目标根节点。
6. 点击空白区域，等待默认 5 秒，飞行投掷物会消失。

## 场景结构与 Inspector

```text
AR Session                          ARSession + ARInputManager
XR Origin (Mobile AR)               XROrigin，单位缩放，Device origin，Y offset = 0
  Camera Offset
    AR Camera                       Camera + ARCameraManager + ARCameraBackground
                                    + TrackedPoseDriver + AudioListener
TargetCube                          单位缩放根节点，运动学 Rigidbody
  TargetShape                       0.6 × 0.4 × 0.2 m，BoxCollider，蓝色材质
OtherCollider (...)                 BoxCollider，红色材质
Projectile Spawner                  ProjectileSpawner + EditorARPreview
Directional Light
```

`Projectile Spawner > ProjectileSpawner` 的引用已经设置：

| 字段 | 默认值 / 引用 | 用途 |
| --- | --- | --- |
| Projectile Prefab | `Prefabs/Projectile.prefab` | 发射实例模板 |
| AR Camera | XR Origin 下的 AR Camera | ScreenPointToRay 与手势发射起点 |
| Selected Target | **TargetCube 根节点** | 每次发射时快照当前指定目标 |
| Throw Speed | 6 m/s | 初速度 |
| Spawn Distance | 0.2 m | 沿屏幕射线从近裁剪面向前生成；手势接口从摄像机位置向前生成 |
| Require Tracking On Device | true | 手机 AR 尚未达到 SessionTracking 时暂停发射 |

`Projectile.prefab > Projectile`：Max Lifetime = **5 s**，Surface Offset = **0.001 m**。存活时间只限制飞行，命中后不会被计时器销毁。

投掷物：边长 **0.04 m**；BoxCollider 非 Trigger；Rigidbody Mass = 0.02，Use Gravity = false，Is Kinematic = false，Continuous Dynamic，Interpolate。命中后归零线速度和角速度，切换为运动学，禁用碰撞检测、Collider 与插值。

目标：运动学 Rigidbody + 子节点 BoxCollider，Continuous Speculative，无重力。外部模块可更新根节点 Transform；连续物理驱动也可在 FixedUpdate 使用 Rigidbody.MovePosition / MoveRotation。

**不依赖 Tag**：用发射时保存的 Transform 引用及 `IsChildOf` 判断，只有该目标层级的 Collider 可以贴附。场景使用 Default Layer；投掷物使用 Projectile Layer，并关闭该层自身碰撞，支持快速连续发射。Projectile 与 Default 的碰撞必须开启。

目标根节点与 XR Origin 请保持正的单位缩放；通过 `TargetShape.localScale` 调整长方体尺寸。目标根节点的非均匀缩放与旋转组合会产生剪切，不能表达为普通立方体 Transform。本原型保证目标根节点的平移、旋转跟随。

## 代码和扩展接口

| 文件 | 职责 |
| --- | --- |
| `Runtime/ProjectileSpawner.cs` | 触摸 / 鼠标输入、屏幕射线、生成投掷物、记录选中目标；移动端只处理触摸，避免模拟鼠标重复发射 |
| `Runtime/Projectile.cs` | Rigidbody 实际飞行、唯一一次碰撞处理、目标层级判断、贴附、错误命中销毁、飞行超时 |
| `Runtime/EditorARPreview.cs` | 仅 Editor 中禁用设备 AR 组件，让同一场景支持桌面物理预览 |
| `Editor/ARPrototypeSetup.cs` | 用 Unity API 创建并保存原型资源、引用、平台与 XR 设置 |
| `Editor/ARPrototypeTestRunner.cs` | 菜单运行 PlayMode 验收测试，并保存 XML 报告 |
| `Tests/PlayMode/ProjectilePhysicsTests.cs` | 实际物理碰撞、表面朝向、跟随、遮挡、超时与连续发射测试 |
| `Tests/PlayMode/PrototypeSceneTests.cs` | 加载已保存场景，验证实际 Prefab、引用、碰撞层与发射贴附 |

未来手势模块拿到 `ProjectileSpawner` 引用后直接调用：

```csharp
// direction 必须是 Unity AR 世界坐标中的方向。
Projectile instance = spawner.Throw(direction);

// 若手势模块输出屏幕位置：
spawner.ThrowAtScreenPoint(screenPosition);

// 外部识别 / 追踪模块更新目标位姿：
spawner.SelectedTarget.SetPositionAndRotation(worldPosition, worldRotation);
```

`Throw` 返回 null 表示引用未配置、方向无效、目标未激活或手机未追踪。发射后切换 Selected Target 不会改变已经飞行的投掷物的目标。

贴附使用实际碰撞接触点和表面法线，规定投掷物的 **局部 -Z 面接触目标，+Z 指向目标外侧**。按 BoxCollider 的半厚度与 Surface Offset 计算中心位置，保存 `InverseTransformPoint` 得到的局部位置及逆目标旋转得到的局部朝向，再设置父节点和局部位姿。原投掷物自身成为贴附物，不另外创建贴纸。

如果接入模块在取消追踪时隐藏 TargetCube，应让它管理该根节点的显隐；已贴附子物体会随之隐藏。需要更换物理表面时，应让 Collider 保持在 Selected Target 的层级内。相机看到的真实环境本身没有 Unity Collider；需要检测真实遮挡物时由其他模块提供对应 Collider。

## Android 真机

1. 使用支持 Google ARCore 的 Android 手机，安装 / 更新 Google Play Services for AR。
2. Unity Hub 确认当前 Editor 安装 **Android Build Support、Android SDK & NDK Tools、OpenJDK**。
3. File > Build Settings > Android > **Switch Platform**。
4. Project Settings > XR Plug-in Management > Android：确认 **ARCore** 已勾选、Initialize XR on Startup 已开启。菜单 **AR Prototype > Configure Mobile Platforms and XR** 可重新应用设置。
5. 已设置 Minimum API Level **24**、**IL2CPP / ARM64**、Graphics API **OpenGLES3**、包名 `com.prototype.arthrow`。若 SDK / NDK 路径缺失，在 Preferences > External Tools 选择 Unity 安装的工具。
6. 确认 Build Settings 首个启用场景为 ARThrowPrototype，连接手机并 Build And Run，首次启动允许摄像头权限。
7. 等待追踪建立，初始朝向前方约 **1.5 m** 可见蓝色目标；移动手机可以绕到不同表面发射。目标使用固定 AR 世界坐标，不要求识别平面。

## iOS 真机

ARKit 插件和 iOS XR Loader 已配置。本机 Windows Editor **未安装 iOS Build Support**。在装有匹配 Unity Editor 和 iOS Build Support 的 Mac 上打开项目：

1. Switch Platform 到 iOS，确认 XR Plug-in Management > iOS 勾选 **ARKit**。
2. 已设置最低 iOS **13.0** 和摄像头用途说明；按项目需要设置自己的 Bundle Identifier。
3. 导出 Xcode 工程，设置开发团队和签名，连接支持 ARKit 的 iPhone 安装运行，允许摄像头权限。

## 验收与报告

菜单 **AR Prototype > Run Physics Acceptance Tests**，或 Window > General > Test Runner > PlayMode > `ARPrototype.PlayModeTests`。

测试结果保存到项目根目录 `TestResults/ARPrototype-PlayMode.xml`。自动化测试通过真实 Rigidbody / BoxCollider 回调验收；测试输入方向时直接调用屏幕坐标接口，不声称已模拟手机触摸或摄像头硬件。桌面加载 AR 场景时，AR Foundation 可能提示没有活动的 Session / Camera / Input 子系统，这是没有启动真机 XR Provider 的提示；Editor 物理预览仍可运行。

| 用户验收项 | 验证方式 |
| --- | --- |
| 1 正面命中 | PlayMode 检查表面位置、父节点、停用物理 |
| 2 斜向及不同表面 | PlayMode 检查正面、侧面、顶面和旋转目标的法线对齐 |
| 3 / 4 目标平移与旋转 | PlayMode 检查世界位姿随目标变化、局部位姿保持 |
| 5 击中其他物体 | PlayMode 检查遮挡后销毁、错误目标引用不贴附 |
| 6 未命中 | PlayMode 检查飞行超时销毁，另检查命中后超过飞行时限仍保留 |
| 7 连续发射 | PlayMode 检查 5 次发射只形成 5 个贴附物，另测试 80 m/s 的连续碰撞检测 |
| 8 移动端输入 | 输入路径已隔离；手机上逐次触摸并观察实例数量，需真机验收 |
| 9 AR 背景与追踪 | 场景与 Loader 已配置；摄像头画面、权限、位姿更新需真机验收 |

自动化结果与本次执行范围另见根目录 `IMPLEMENTATION_REPORT.md`。
