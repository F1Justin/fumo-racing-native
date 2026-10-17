# 源码导出与 GitHub 分发

公开仓库为 [F1Justin/fumo-racing-native](https://github.com/F1Justin/fumo-racing-native)。目前提供开发源码，正式安装包将在完整导入与游戏运行验收后放入 GitHub Releases。

## 导出源码

从白名单导出到新的空目录，再用于公开仓库：

```sh
python3 publish/export_public.py --output /absolute/new-public-source --report /absolute/export-audit.json
python3 publish/audit.py /absolute/new-public-source --mode source
```

`allowlist.json` 列出启动器、导入器、构建工具与文档。导出会检查路径、文件类型和敏感信息，并保留完整文件清单。原版资源、恢复的 Unity 工程、本机构建输出、签名凭据及现有 Git 历史留在工作区。新增源码文件需更新白名单。

项目源码许可证状态见 `SOURCE-LICENSE-NOTICE.txt`，依赖说明见 `THIRD-PARTY-NOTICES.md`。

加入运行代码时，先使用 `unity/public-source-export.py` 导出已审核文件，再执行 `audit.py PUBLIC_ROOT --mode source --runtime-source runtime-source`。此选项只允许清单中逐项核对的代码、许可证和元数据，目录里的文件集合及哈希必须完全相符。

## 组装与安装验收

先组装供实测的完整候选内容：

```sh
python3 publish/assemble_recipe.py stage --recipe-dir /absolute/draft \
  --runtime-dir /absolute/Game.app --runtime-output Game.app \
  --runtime-executable Contents/MacOS/Game \
  --reconstruction-report /absolute/draft/audit.private.json \
  --runtime-receipt /absolute/public-player-build-receipt.json \
  --runtime-file-inventory /absolute/final-player-receipt.private.json \
  --licenses /absolute/runtime-notices --output /absolute/candidate
```

stage 要求资源重建未解决项为 0，并核对原生程序的构建记录与完整文件清单。运行文件逐项比对大小和 SHA-256，相对链接也须明确列入清单；许可证支持文本和 PDF。运行文件以哈希命名，权限和相对链接另列在安装清单中，安装时恢复完整目录。候选内容保持 draft 状态，供维护者使用开发入口测试。

实测记录使用 schemaVersion 1，绑定输出的 `candidateRecipeSHA256`、`contentId`、`platform` 和 `sourceInventorySHA256`。完成相应工作后填写 `originalPackageImportVerified`、`playableGameVerified`、`sourceProvenanceReviewed`；`unresolvedCount` 与 `originalContentObjectsBundled` 均须为 0。

```sh
python3 publish/assemble_recipe.py finalize --recipe-dir /absolute/candidate \
  --verification /absolute/game-verification.json --output /absolute/ready-installation
python3 launcher/build.py --target macos-arm64 --importer /absolute/content-importer \
  --recipe-dir /absolute/ready-installation --output /absolute/launcher-package
```

finalize 生成绑定实测候选内容的验收摘要，将状态改为 ready。启动器打包时再次检查全部文件及该摘要。私有报告和实测日志留在构建环境，只把相应哈希写入安装内容。

## 检查安装包

```sh
python3 publish/audit.py /absolute/final-release-assets --mode release --review /absolute/review.json --report /absolute/payload-audit.json
```

审核记录使用 schemaVersion 1，包含已完成的来源审核、原包导入验收和分发批准：`sourceProvenanceReviewed`、`originalPackageImportVerified`、`publicDistributionAccepted` 均为 true，`originalContentObjectsBundled` 为 0，`sourceInventorySHA256` 对应审核过的源码清单。

`files` 按安装包相对路径记录 bytes、SHA-256、许可证说明和类别：`project-code`、`third-party-runtime`、`reconstruction-recipe`、`notice` 或 `checksum`。文件变化后需重新检查并更新记录。

扫描覆盖敏感文件名、令牌与私钥、路径越界以及 ZIP 内的文件和链接。Unity 内容来源与重建结果由单独的内容清单和实际导入测试确认。

## 创建 Release 草稿

`templates/draft-release.yml` 位于工作流目录之外，供维护者审核后启用。工作流使用现有 tag，读取本仓库的 `reviewed-release` 构建产物，核对审核记录后创建 draft prerelease。

该构建产物应包含：

- `payload/`：各平台安装包、SHA256SUMS 及第三方说明。
- `review.json`：最终文件的审核记录。
- `release-notes.md`：支持系统、原版版本、安装步骤和已知问题。

维护者确认完整资产后发布。启用不可变发行版时，应先把全部文件加入草稿；流程见 [GitHub 官方说明](https://docs.github.com/en/code-security/concepts/supply-chain-security/immutable-releases)。

## 验证范围

```sh
python3 -m unittest discover -s publish/tests -v
```

自动检查覆盖白名单导出、敏感文件、篡改文件和不安全归档。实际原包导入、游戏运行、许可证审核及各平台安装体验由发行验收记录完成。
