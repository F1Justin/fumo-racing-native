# 原版游戏导入程序

导入程序读取玩家提供的 Touhou Fumo Racing 0.2.1 ZIP 或已解压文件夹，检查版本与文件完整性，在本机安装游戏。成功后启动器会记住安装位置；失败时保留已有安装，显示原因和下一步操作。玩家使用发行包内的独立程序。

## 开发接口

```sh
python -m content_importer --source /absolute/original.zip --output /absolute/new-install --recipe /absolute/release-directory --json-progress
```

`--recipe` 指定包含 `recipe.json` 及其列出的安装文件的目录。stdout 使用 UTF-8 JSONL 向启动器报告进度。成功结果包含 installation、executable 和固定的内容路径参数；失败返回非零状态及适合玩家阅读的中文错误，技术诊断写入 stderr。

安装先在临时目录完成检查和重建，再提交到目标位置。导入器只接受发行时核对过的原版文件与安装内容。

## 构建独立程序

在各目标系统和架构上分别构建。维护者使用隔离的 Python 环境安装固定版本依赖：

```sh
python -m venv /absolute/build-environment
# 激活该环境后运行：
python -m pip install -r requirements-build.txt
python build.py --output /absolute/new-importer-output
```

输出包括 `content-importer[.exe]`、构建记录和第三方许可证。构建记录列出输入模块、文件哈希及测试结果。音频转换直接保留原包中的 FSB 数据；打包过程排除可选的 FMOD 解码模块和 SDK。

macOS 构建会检查可执行文件与全部嵌入依赖的最低系统版本，启动器据此填写 Info.plist。已验证的 CPython 3.13.16 运行时来自 [Python Build Standalone](https://github.com/astral-sh/python-build-standalone/releases) 的 `20261013` 发行，架构为 `aarch64-apple-darwin`；其二进制要求 macOS 11.0，与当前 Go 启动器组合后要求 macOS 13.0。最低系统的实际运行测试仍需完成。

使用该便携 Python 时，应核对 install-only 和 matching full 两个官方归档的 SHA-256。full 归档中的 `python/licenses/` 包含静态扩展库的许可证；把它们放到隔离运行时的 `licenses/`，并在同级 `python-runtime-provenance.json` 记录归档地址和哈希，构建时即可一并收集。

Windows 导入器保留标准输出，控制台窗口由启动器隐藏。Linux 在拟支持的最旧用户环境中构建。PyInstaller 的平台要求见[官方说明](https://pyinstaller.org/en/stable/operating-mode.html)。

## 草地重建

`grass.py` 从玩家原包模型的顶点生成项目的三片草叶结构。维护工具 `producer/grass_mapping.py` 比对顶点、索引和其他模型字段，导出原模型引用、顶点编号及渲染设置。当前 557 个草地模型的顶点与索引字节全部一致，范围值允许 float32 运算误差。

## 验证范围

自动测试覆盖输入路径、ZIP 解包、版本检查、失败回滚和草地生成。独立程序已通过启动与开发包拒绝测试；启动器也已通过调用该程序并显示中文错误的测试。完整原包导入和游戏运行在发行前统一验收。
