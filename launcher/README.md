# 导入原版游戏

启动工具用于导入 Touhou Fumo Racing 0.2.1，并启动安装好的游戏。玩家下载对应系统的发行包，打开工具，选择原版 ZIP 或已解压文件夹；导入完成后点击“开始游戏”。以后打开工具可直接启动上次安装。

macOS 使用原生窗口，Windows 和 Linux 使用本机浏览器界面。文件选择使用系统对话框；Linux 没有 zenity 或 kdialog 时，可在“高级选项”填写路径。玩家所需的导入程序随发行包提供。

默认安装位置：

| 系统 | 位置 |
|---|---|
| macOS | `~/Library/Application Support/Fumo Racing/Installations/current` |
| Windows | `%LOCALAPPDATA%/Fumo Racing/Installations/current` |
| Linux | `$XDG_DATA_HOME/Fumo Racing/Installations/current`，默认 `~/.local/share` |

## 构建

维护者需要 Go 和 Python 3；构建 macOS 窗口还需要 Xcode command-line tools。

```sh
python3 build.py --target all
python3 build.py --target macos-arm64 --importer /absolute/content-importer --recipe-dir /absolute/ready-installation
```

输出在 `dist/<target>`。第一条命令构建各平台启动界面，第二条命令附入单个平台的导入器和完整安装内容。导入器旁须有 `importer-build-receipt.json` 和 `THIRD-PARTY-LICENSES/`；脚本核对平台、架构、文件哈希和许可证清单。安装目录须经 `publish/assemble_recipe.py finalize` 完成验收，目录内文件必须与清单完全一致。

macOS 最低版本取 Go 引擎与导入器所有嵌入依赖的最高要求，写入 Info.plist。当前组合要求 macOS 13.0。本地构建采用 ad-hoc 签名；正式分发还需完成 Developer ID 签名、公证及最低系统实机测试。Windows 代码签名也在发行验收范围内。

## 维护者接口

启动器执行发行包内固定的程序和数据目录：

```text
content-importer --source PATH --output PATH --json-progress --recipe FIXED_DIRECTORY
```

普通平台把启动器、`content-importer[.exe]`、`recipe.json` 放在同一目录。macOS 的可执行文件位于 `.app/Contents/MacOS`，`recipe.json` 及其引用的文件位于 `.app/Contents/Resources`。`--recipe` 接受包含该 JSON 文件的目录。

导入器通过 stdout 输出 UTF-8 JSONL，单行上限 64 KiB：

```json
{"type":"progress","phase":"checking","completed":1,"total":5,"message":"正在检查原版游戏…"}
{"type":"result","installation":"/absolute/output","executable":"/absolute/output/Game.app/Contents/MacOS/Game","arguments":[]}
```

错误使用 `type:error` 和中文 `message`，并以非零状态退出。进度没有有效 total 时显示不确定进度。启动器要求退出码为 0、唯一 result、有效的安装记录及可执行文件，随后保存安装位置。

所有结果路径经符号链接解析后检查：installation 必须位于 output 内，executable 和 `Content/install-manifest.json` 必须位于安装内。参数仅允许空数组，或固定的 `-fumo-content-dir` 与 Content 路径。启动时设置 `FUMO_CONTENT_PATH`。导入器负责内容哈希验证；启动器检查安装文件是否仍在原位置。

本机接口监听 `127.0.0.1` 动态端口，使用随机会话 token 并校验 Host、Origin 和 `X-Fumo-Token`。界面资源全部嵌入程序。

| 接口 | 用途 |
|---|---|
| `GET /api/state` | 读取进度、状态及启动条件 |
| `POST /api/select` | 选择 ZIP、原版文件夹或安装位置 |
| `POST /api/install` | 传入 `sourcePath` 和可选 `outputPath`，开始导入 |
| `POST /api/launch` | 启动已安装游戏 |
| `POST /api/quit` | 安装空闲时退出工具 |

## 验证范围

```sh
go test -race ./...
```

自动测试覆盖进度、失败处理、安装记录、路径边界和启动参数；macOS 原生窗口已用模拟导入器验证选择、进度和启动流程，独立导入器也已通过真实进程调用的错误处理测试。完整原包导入、游戏运行和下载后的首次启动仍由发行验收完成。
