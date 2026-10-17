# 原版资源导入与启动工具

此目录是独立首装启动器，不依赖更新器模块，不修改游戏和 updater，也不包含原作图片、字体、音乐、图标或游戏数据。

运行端为 Go 单文件程序，macOS 外加 AppKit 原生窗口，Windows/Linux 当前打开本机浏览器界面。Windows 的文件选择使用系统 PowerShell/WinForms；Linux 优先使用 zenity 或 kdialog，无选择器时提供路径输入。最终发布还需为每个平台附上冻结的 `content-importer`，玩家不安装 Python。

## 构建

开发构建需要 Go、Python 3；macOS 壳还需要 Xcode command-line tools。源码只用 Go 标准库。

```sh
python3 build.py --target all
# 将经过审核的导入器和配方附入单个平台；不上传、不创建 Release：
python3 build.py --target macos-arm64 --importer /absolute/content-importer --recipe /absolute/recipe.json
```

默认输出 `dist/<target>`。未提供导入器与配方时仅构建窗口骨架，不能安装游戏。macOS 分 arm64、amd64 两个包，使用本地 ad-hoc 签名，尚未 Developer ID 签名或公证。Windows 尚未 Authenticode 签名。receipt 中 `publicDistributionAccepted`、`realGameTested` 默认均为 false。

macOS 最低系统声明从生成的 Go engine 的 Mach-O deployment target 读取，再用于 AppKit host 和 Info.plist；当前本机 Go 1.27.1 输出要求 macOS 13.0。最终导入器和游戏的要求也应纳入发行支持矩阵。

普通平台布局：`Fumo-Racing-Launcher[.exe]`、`content-importer[.exe]`、`recipe.json` 相邻。macOS 的 engine 与 content-importer 在 `.app/Contents/MacOS`；recipe.json 在 `.app/Contents/Resources`。配方引用的其它已审核文件由发行打包层按导入器约定提供；此构建脚本不自行收集任何游戏目录。

## 导入器协议

启动器只执行包内固定导入器，不通过 shell，UI 不允许修改导入器、配方或命令参数。开发测试可用 `--bundle-dir` 指定夹具目录；不要在玩家说明中使用它。

```text
content-importer --source PATH --output PATH --json-progress --recipe FIXED_RECIPE_DIRECTORY
```

导入器 stdout 逐行输出 UTF-8 JSON，单行上限 64 KiB：

```json
{"type":"progress","phase":"checking","completed":1,"total":5,"message":"正在检查原版游戏…"}
{"type":"result","installation":"/absolute/output","executable":"/absolute/output/Game.app/Contents/MacOS/Game","arguments":[]}
```

进度 total 缺省或小于等于 0 时为不确定进度。错误可输出 `{"type":"error","message":"友好错误原因"}`，并以非零状态退出。stderr 最多保留末尾 8 KiB。只有退出码为 0、恰有一个 result、文件与路径通过校验时才记住安装。不得在 stdout 混入普通日志。

`--recipe` 传入包含 `recipe.json` 的目录；启动器预先检查固定 JSON 文件，再把其父目录传给导入器。发行配方引用的文件也必须按配方相对路径放在该目录下。

结果的 installation 必须在 output 内，executable 必须在 installation 内。路径必须为绝对路径，经符号链接解析后再次检查；文件必须真实存在且在 Unix 有执行位。还要求安装内有 `Content/install-manifest.json`。`arguments` 仅允许空数组或 `['-fumo-content-dir', '<canonical installation>/Content']`；其它参数拒绝。

启动器设置 `FUMO_CONTENT_PATH=<installation>/Content`，并过滤父进程同名变量。内容 manifest 的 schema、各 bundle 哈希及内容完整性由导入器负责验证；启动器当前只检查安装记录文件存在，不把存在性视为哈希验证。

默认安装：macOS `~/Library/Application Support/Fumo Racing/Installations/current`；Windows `%LOCALAPPDATA%/Fumo Racing/Installations/current`；Linux `$XDG_DATA_HOME/Fumo Racing/Installations/current`（缺省 `~/.local/share`）。安装记录保存在同一级数据根下的 `Fumo Racing Launcher/installation.json`，与游戏和原包分离。

## 本机接口与验证

只监听 `127.0.0.1` 动态端口；每次随机生成会话 token，并校验 Host、Origin 和 `X-Fumo-Token`。所有界面内容嵌入程序，不引用远程字体、脚本或图像。接口不接受 recipe/importer/额外命令参数。

- `GET /api/state`：phase、busy、progress、indeterminate、message、error、outputPath、installation、canLaunch。
- `POST /api/select`：`{"kind":"sourceZip|sourceDirectory|outputDirectory"}`，取消返回空路径。
- `POST /api/install`：`{"sourcePath":"...","outputPath":"..."}`；outputPath 可省略。
- `POST /api/launch`：启动已验证的安装。
- `POST /api/quit`：非安装状态下退出浏览器模式后台进程。

```sh
GOCACHE=/tmp/fumo-launcher-go-cache go test -race ./...
```

进程测试使用模拟导入器，不包含原作资源、不会生成真实游戏，不等于真实游戏安装或平台实机验收。正式发布还须进行冻结导入器、实际原包、最终游戏与下载后的首次启动验收。
