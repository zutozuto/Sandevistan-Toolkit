# 斯安威斯坦 · Sandevistan Toolkit

by：**ずっと子都**

面向 VRChat PC Avatar 的 Unity 残影工具。角色移动时按距离留下历史骨骼姿势，停步后已有残影逐渐消失。支持减面代理和保留原网格及主色贴图，可预览透明度、渐变染色、菲涅尔发光和发光线纹。

[下载 v5.7.2 Unity 安装包](https://github.com/zutozuto/Sandevistan-Toolkit/releases/download/v5.7.2/Sandevistan_Toolkit_v5.7.2_PC.unitypackage)

## 快速开始

1. 使用 VCC 的 PC Avatar 工程，并安装 VRChat Avatars SDK、Modular Avatar 和 NDMF。工具在 Unity 2022.3.22f1 上验证。
2. 安装Gesture Manager ，Modular Avatar和Liltoon
3. 将 `.unitypackage` 导入 Unity。选中有 Humanoid Animator 的角色根节点，打开 `Tools > Sandevistan...`。
4. 在「安装」页勾选身体、头发和需要跟随的服装、道具，设置最大残影数、面数和间距上限后安装。再次选中同一个已安装角色时，可读取设置并直接更新。
5. 可以在 Unity 中用 Gesture Manager 和「移动测试」页试跑。上传前用 VRChat SDK 的 Build & Test 核对效果与 SDK 性能评级。
6. 在 Avatar 的表情菜单里打开「斯安威斯坦」。可选择触发方式和七套渐变，并通过轮盘调整残影数量、间距与存留时间。

默认安装 3 道残影，容量可设为 3–10 道。减面模式默认每道目标 8,000 三角面，实际面数以安装报告为准。SDK 性能评级会计入已经安装的残影池，游戏内的数量轮盘只控制使用数量。

[使用说明及性能设置](Assets/Sandevistan/README.zh-CN.md) · [材质与自定义纹理](Assets/Sandevistan/Material-guide.zh-CN.md) · [发布记录](https://github.com/zutozuto/Sandevistan-Toolkit/releases)
