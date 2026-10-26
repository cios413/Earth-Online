# AR 投掷贴附原型交付记录

## 完成情况

已直接在当前项目安装 AR 包、创建 C# 脚本，并由 Unity Editor 实际生成和保存材质、Projectile Prefab、ARThrowPrototype 场景以及移动端 XR Loader 设置。场景引用已连接，已列为第一个启用的构建场景。

原型支持屏幕位置决定发射方向，Rigidbody 运动触发真实碰撞，只有发射时指定的目标层级允许贴附。贴附后立方体跟随目标根节点平移和旋转；错误命中销毁，未命中超时销毁。手势扩展接口为 `ProjectileSpawner.Throw(Vector3 worldDirection)`。

## 初始检查与最终配置

| 项目 | 初始状态 | 最终状态 |
| --- | --- | --- |
| Unity | 2022.3.53f1c1 | 使用同一版本完成编译与 PlayMode 测试 |
| 场景 | 默认 SampleScene，普通 Camera / Light | 新增 ARThrowPrototype：AR Session、XR Origin、AR Camera、虚拟目标、非目标遮挡物、Spawner |
| 输入 | Input Manager (Old)，activeInputHandler = 0 | 保留旧输入；Editor 鼠标、手机仅触摸，按 TouchPhase.Began 发射 |
| AR Foundation / ARCore / ARKit | 未安装 | 均为 5.1.6，包管理器已解析 |
| XR Plug-in Management | 未配置 | 4.4.0，Android ARCore / iOS ARKit Loader 均已保存，自动启动开启 |
| Android | Min SDK 22，ARMv7，未配置后端 | Min SDK 24，ARM64，IL2CPP，OpenGLES3 |
| iOS | 没有摄像头说明，最低 12.0 | 已配置摄像头用途说明、最低 13.0、ARKit Loader |
| Android 工具链 | 已安装 Android Build Support | 已确认 SDK 中 adb、NDK、OpenJDK 均存在 |
| iOS 工具链 | 未安装 iOS Build Support，当前为 Windows | 仍需 Mac + 匹配 Editor 的 iOS Build Support + Xcode 进行设备构建 |
| 构建场景 | 空列表 | ARThrowPrototype 已启用 |

## 文件清单

新增主体文件位于 `Assets/ARPrototype/`（Unity 已生成对应 `.meta`）：

- `Runtime/Projectile.cs`：碰撞状态机、目标引用判断、接触点贴附、生命周期。
- `Runtime/ProjectileSpawner.cs`：输入、ScreenPointToRay、可供手势调用的 Throw 接口。
- `Runtime/EditorARPreview.cs`：同一 AR Camera 的 Editor 物理预览。
- `Runtime/ARPrototype.Runtime.asmdef`：运行时程序集。
- `Editor/ARPrototypeSetup.cs`：资源与平台设置生成菜单。
- `Editor/ARPrototypeTestRunner.cs`：测试运行菜单及 XML 输出。
- `Editor/ARPrototype.Editor.asmdef`：Editor 程序集。
- `Tests/PlayMode/ProjectilePhysicsTests.cs`：11 项核心行为测试。
- `Tests/PlayMode/PrototypeSceneTests.cs`：1 项实际场景 / Prefab 集成测试。
- `Tests/PlayMode/ARPrototype.PlayModeTests.asmdef`：测试程序集，不进入正常 Player 构建。
- `Scenes/ARThrowPrototype.unity`：可直接打开的场景。
- `Prefabs/Projectile.prefab`：已配置 Rigidbody / BoxCollider / Projectile 的立方体。
- `Materials/Projectile.mat`、`Target.mat`、`Obstacle.mat`：纯色材质。
- `README.md`：Inspector 参数、层级、测试及移动端运行说明。

Unity 新增的 XR 资源位于 `Assets/XR/`，包含 ARCore / ARKit Loader、Provider Settings、ARPrototypeXRSettings 和 AR Foundation 自动生成的 Simulation 设置。

修改的项目配置：`Packages/manifest.json`、包管理器生成的 `Packages/packages-lock.json`、`ProjectSettings/ProjectSettings.asset`、`EditorBuildSettings.asset`、`TagManager.asset` 和 `DynamicsManager.asset`。新增 Projectile Layer，并保存该层不与自身碰撞的物理矩阵；目标身份使用 Transform 引用，不依赖 Tag。

测试输出：`TestResults/ARPrototype-PlayMode.xml`。

## 已执行验证

在正在运行的 Unity **2022.3.53f1c1** Editor 中完成脚本编译，并通过 Unity Test Runner 执行 **12 项 PlayMode 测试：12 通过、0 失败、0 跳过**。

| 行为 | 结果 |
| --- | --- |
| 正面命中、按半厚度贴在表面、禁用物理 | 通过 |
| 斜向发射命中正面 / 侧面 / 顶面 | 通过 |
| 目标平移与旋转后的局部位姿保持 | 通过 |
| 旋转目标的接触法线与局部坐标换算 | 通过 |
| 前方遮挡物截获后销毁、不继续命中目标 | 通过 |
| 命中非指定目标不贴附 | 通过 |
| 未命中后按寿命销毁 | 通过 |
| 已贴附对象超过飞行寿命仍保留 | 通过 |
| 不同屏幕位置得到不同速度方向，发射时快照目标引用 | 通过 |
| 80 m/s 高速物理碰撞 | 通过（该测试场景与目标厚度） |
| 5 次连续发射只形成 5 个贴附物 | 通过 |
| 加载保存的 AR 场景，使用实际 Prefab 与引用发射并贴附 | 通过 |

另已核对保存后的 Prefab 无重力、动态 Rigidbody、Continuous Dynamic、BoxCollider 非 Trigger；目标根节点使用单位缩放，外观子节点为长方体。Android / iOS 的 Loader 资产引用和构建场景已保存。

## 仍需人工执行的设备验收

`adb devices -l` 当前未发现连接的 Android 设备，因此没有执行手机安装、真实触摸去重、摄像头权限、摄像头背景或 AR 位姿追踪验收，也没有生成 APK。当前交付为 Unity 项目内可运行原型。

Android：切换平台到 Android，确认 XR Plug-in Management 启用 ARCore，连接支持 ARCore 的手机，Build And Run 并允许摄像头权限。等待追踪建立后点击目标；逐次点击检查每次只生成一个投掷物，再移动手机从不同表面发射。

iOS：在 Mac 上安装匹配版本的 Unity iOS Build Support，切到 iOS，确认 ARKit Loader，导出 Xcode 工程并完成开发团队签名后安装到支持 ARKit 的 iPhone。

Editor 预览显示纯色背景；手机运行使用真实 AR Camera 背景及 TrackedPoseDriver。桌面没有活动 XR 子系统时，AR Foundation 的相关提示不代表手机 Provider 已失效。详细操作与验收步骤见 `Assets/ARPrototype/README.md`。
