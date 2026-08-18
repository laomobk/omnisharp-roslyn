# 在 Termux 中构建并使用 OmniSharp

本文说明如何在 Android 的 Termux 环境中，从本压缩包构建 `net8.0` 版
OmniSharp，并让 `omnisharp-vim` 通过标准输入输出协议启动它。

本仓库已经移除 Mono、`net472` 和服务端的 `net6.0` 多目标构建。OmniSharp
服务端需要可用的 .NET 8 SDK/Runtime。

## 1. 环境要求

- 建议使用 F-Droid 或 GitHub 发布的新版 Termux，不要使用已停止维护的
  Google Play 旧版 Termux。
- 手机通常应为 `aarch64`。运行 `uname -m` 确认。
- 至少预留约 2 GB 存储空间。首次恢复 NuGet 包需要联网。
- 源码和 C# 项目最好放在 Termux 私有目录 `$HOME`，不要直接在 `/sdcard`
  中编译。Android 共享存储不支持完整的 Unix 权限、符号链接和可执行位。

更新 Termux 并安装基础工具：

```sh
pkg update
pkg upgrade
pkg install git unzip clang pkg-config
```

搜索当前仓库提供的 .NET 包：

```sh
pkg search dotnet
```

不同 Termux 软件源的包名可能不同。常见的安装方式是：

```sh
pkg install tur-repo
pkg install dotnet-sdk-8.0
```

如果上述包名不存在，请从你信任的 Termux 仓库安装支持 Android/Bionic 的
.NET 8 SDK。不要安装普通 Linux/glibc 版 SDK。

确认 SDK 可用：

```sh
dotnet --version
dotnet --list-sdks
dotnet --list-runtimes
dotnet --info
```

`dotnet --version` 应显示兼容的 `8.0.x` SDK。本仓库的 `global.json` 请求
8.0.128，并允许滚动到同一 SDK 特性带中的较新补丁版本。`dotnet
--list-runtimes` 必须包含 `Microsoft.NETCore.App 8.0.x`；只有 .NET 9/10
Runtime 不够。

## 2. 解压源码

假设压缩包位于手机的 Download 目录。首次访问共享存储时执行：

```sh
termux-setup-storage
```

授权后解压到 Termux 私有目录：

```sh
mkdir -p "$HOME/src"
cd "$HOME/src"
unzip "$HOME/storage/downloads/omnisharp-roslyn-net8-termux-source.zip"
cd omnisharp-roslyn-net8-termux
```

如果下载后的文件名不同，请相应替换 `unzip` 后面的路径。

## 3. 一键构建并安装（推荐）

源码根目录已经附带 `termux-install.sh`。在 Termux 中直接运行：

```sh
cd "$HOME/src/omnisharp-roslyn-net8-termux"
bash termux-install.sh
```

脚本会检查 .NET 8 SDK 和 Runtime，自动设置 `DOTNET_ROOT`，清理并重建
`$PREFIX/lib/omnisharp`，在 `aarch64` 上优先尝试 `linux-bionic-arm64`，
如果当前 Termux 软件源没有该 Runtime Pack 就自动改用不带 RID 的可移植发布。
最后会生成 `$PREFIX/bin/omnisharp-termux` 并运行 `--help` 烟雾测试。

脚本可以重复执行。它只会删除并重建 `$PREFIX/lib/omnisharp`，不会修改
`.vimrc`，也不会删除你的项目或 Vim 插件。

## 4. 手工发布 Termux/Bionic 版本

`omnisharp-vim` 使用标准输入输出服务端，因此只需要发布
`OmniSharp.Stdio.Driver`，不需要构建测试项目、HTTP Driver 或运行 Cake
发布流水线。

在仓库根目录运行：

```sh
export RID=linux-bionic-arm64
export OMNISHARP_HOME="$PREFIX/lib/omnisharp"

mkdir -p "$OMNISHARP_HOME"

dotnet publish \
  src/OmniSharp.Stdio.Driver/OmniSharp.Stdio.Driver.csproj \
  --configuration Release \
  --framework net8.0 \
  --runtime "$RID" \
  --self-contained false \
  -p:PublishReadyToRun=false \
  -p:UseAppHost=false \
  -p:RollForward=LatestMinor \
  --output "$OMNISHARP_HOME"
```

说明：

- `linux-bionic-arm64` 对应常见的 64 位 Android/Termux 环境。
- 不要改成普通的 `linux-arm64`。普通 Linux 产物通常依赖 glibc，而
  Android 使用 Bionic libc。
- `--self-contained false` 表示使用 Termux 已安装的 .NET Runtime。
- `RollForward=LatestMinor` 允许使用较新的 .NET 8 Runtime，但不会跨主版本
  改用 .NET 9/10。
- `PublishReadyToRun=false` 避免 Android 上不必要的跨平台预编译问题。
- `UseAppHost=false` 只生成可移植的 `OmniSharp.dll`，由包装脚本通过
  `dotnet` 启动。

发布完成后应至少存在以下文件：

```sh
ls -l "$OMNISHARP_HOME/OmniSharp.dll"
ls -l "$OMNISHARP_HOME/OmniSharp.deps.json"
ls -l "$OMNISHARP_HOME/OmniSharp.runtimeconfig.json"
```

验证服务端能够加载：

```sh
dotnet "$OMNISHARP_HOME/OmniSharp.dll" --help
```

### RID 不受支持时的回退方案

如果发布出现 `NETSDK1083`、找不到 `linux-bionic-arm64` Runtime Pack，或
当前 Termux .NET 包没有注册该 RID，可以生成不指定 RID 的可移植版本：

```sh
export OMNISHARP_HOME="$PREFIX/lib/omnisharp"

dotnet publish \
  src/OmniSharp.Stdio.Driver/OmniSharp.Stdio.Driver.csproj \
  --configuration Release \
  --framework net8.0 \
  --self-contained false \
  -p:PublishReadyToRun=false \
  -p:UseAppHost=false \
  -p:RollForward=LatestMinor \
  --output "$OMNISHARP_HOME"
```

包装脚本仍然使用同一个 `OmniSharp.dll`，后续配置不变。

## 5. 创建启动包装脚本

`omnisharp-vim` 的 `g:OmniSharp_server_path` 接收一个可执行路径，而实际
服务端是 .NET DLL。创建一个负责调用 `dotnet` 并转发全部参数的脚本：

```sh
cat > "$PREFIX/bin/omnisharp-termux" <<'EOF'
#!/data/data/com.termux/files/usr/bin/sh
export DOTNET_ROOT="$PREFIX/lib/dotnet"
export DOTNET_ROLL_FORWARD=LatestMinor
exec "$PREFIX/bin/dotnet" "$PREFIX/lib/omnisharp/OmniSharp.dll" "$@"
EOF

chmod +x "$PREFIX/bin/omnisharp-termux"
```

验证包装脚本：

```sh
omnisharp-termux --help
```

也可以确认完整路径：

```sh
command -v omnisharp-termux
```

正常结果通常是：

```text
/data/data/com.termux/files/usr/bin/omnisharp-termux
```

## 6. 配置 omnisharp-vim

确保 Vim 支持异步 Job 和 Channel：

```sh
vim --version | grep -E '\+job|\+channel'
```

在 `~/.vimrc` 中加入：

```vim
let g:OmniSharp_server_path = '/data/data/com.termux/files/usr/bin/omnisharp-termux'
let g:OmniSharp_server_stdio = 1
```

不要在包装脚本中添加 `-lsp`。`omnisharp-vim` 使用 OmniSharp 自己的
STDIO 协议；`-lsp` 是提供给其他 LSP 客户端的模式。

进入包含 `.sln` 或 `.csproj` 的 C# 项目根目录再启动 Vim：

```sh
cd "$HOME/src/your-csharp-project"
vim
```

在 Vim 中打开一个 `.cs` 文件。若插件没有自动启动服务端，可以执行：

```vim
:OmniSharpStartServer
```

常用诊断命令：

```vim
:OmniSharpRestartServer
:OmniSharpOpenLog
```

## 7. 手工诊断服务端

先确认帮助命令正常：

```sh
omnisharp-termux --help
```

需要观察详细启动日志时，在 C# 项目根目录运行：

```sh
omnisharp-termux -s "$PWD" -l Debug
```

服务端启动后等待标准输入是正常现象。按 `Ctrl+C` 结束手工测试。

如果 Vim 中无法启动：

1. 检查 `g:OmniSharp_server_path` 是否为绝对路径。
2. 运行 `ls -l "$PREFIX/bin/omnisharp-termux"`，确认脚本具有执行权限。
3. 运行 `dotnet --list-sdks`，确认安装的是 .NET 8 SDK。
4. 运行 `dotnet --list-runtimes`，确认存在 `Microsoft.NETCore.App 8.0.x`。
5. 运行 `dotnet "$PREFIX/lib/omnisharp/OmniSharp.dll" --help`，直接查看
   缺少的共享框架或本机库。
6. 不要从 `/sdcard` 直接运行生成文件；安装目录应位于 `$PREFIX` 或
   `$HOME`。
7. Android 可能限制 Termux 后台进程。长时间使用时可以关闭 Termux 的
   电池优化限制。

如果日志中出现 `MarshalDirectiveException: Array size control parameter
must be an integral type`，说明仍在运行旧版本的 SDK provider。删除旧安装
目录后重新解压源码，并重新执行 `bash termux-install.sh`；新版会在
`Microsoft.Build.Locator` 的原生枚举失败时扫描 `$PREFIX/lib/dotnet/sdk`。

如果日志中出现下面任一错误，说明仍在使用旧源码构建的 Roslyn DLL 组合：

```text
ShadowCopyAnalyzerAssemblyLoader ... MissingMethodException
AnalyzerAssemblyLoader.CreateNonLockingLoader ... Method not found
OmniSharpLineFormattingOptions.set_NewLine ... Method not found
```

删除旧安装目录，解压本压缩包的最新版本，然后从第 3 节重新发布。不要只
修改启动脚本；此错误还需要重新编译源码：

```sh
rm -rf "$PREFIX/lib/omnisharp"
mkdir -p "$PREFIX/lib/omnisharp"
```

## 8. 更新、重新构建和卸载

更新源码后重新执行第 3 节的 `dotnet publish` 命令即可覆盖安装。
需要完全重新生成时，先删除安装目录：

```sh
rm -rf "$PREFIX/lib/omnisharp"
mkdir -p "$PREFIX/lib/omnisharp"
```

然后重新运行发布命令。

卸载本地 OmniSharp：

```sh
rm -rf "$PREFIX/lib/omnisharp"
rm -f "$PREFIX/bin/omnisharp-termux"
```

这些命令只删除本文创建的 OmniSharp 安装目录和包装脚本，不会删除你的
C# 项目或 `omnisharp-vim` 插件。

## 9. 支持边界

- OmniSharp 服务端自身只运行在 `net8.0`。
- 服务端仍可分析目标为 `net6.0`、`netstandard2.0` 等旧 TFM 的项目，前提
  是项目需要的 Targeting Pack/SDK 在 Termux 中可用。
- 已移除 Mono 和 `net472` 运行路径，传统 .NET Framework 项目不能依靠
  Mono 兼容路径加载。
- Android/Termux 不是 OmniSharp 的主流官方发布环境。如果某个 NuGet
  依赖缺少 Bionic 资产，优先尝试上面的无 RID 可移植发布方式。
