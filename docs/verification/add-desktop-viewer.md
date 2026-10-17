# add-desktop-viewer 手动验收

本文对应 `openspec/changes/add-desktop-viewer/tasks.md` 的 9.6、10.1、10.2，以及 9.7 里“验收清单有对应检查项”的部分（PORT 系列），同时收纳三处并入这里的人工项：5.6（真实拖放）、5.7（Windows 和 Linux 上的链接与新窗口拦截实验）、7.10（焦点在正文里时 ⌘W 仍能关闭标签页）。

它既是操作手册，也是验收记录。顺序是：按第 1 章准备环境，按第 3 到 5 章逐项操作，把结果填进第 2 章的总表，失败的写进第 6 章。

## 怎么用这份文档

- 每条检查项有固定编号（例如 `OPEN-03`），写明对应的规格场景、用到的样本、操作步骤和预期结果。样本路径一律写成 `samples/...`，指你在测试机上准备好的样本目录（见 1.5）。
- 界面文字以英文界面为准，括号里是简体中文界面的对应文字。
- 第 2 章总表的单元格含义：
  - `✅` 通过。
  - `❌` 失败。必须在第 6 章登记一条，并保留截图。
  - `N/A` 此平台上没有对应功能，或本项在此平台上无需执行。总表里已经预填了结构性的 `N/A`，其余的 `N/A` 请在备注里写原因。
  - `CI` 由自动化测试覆盖，不需要手动填写（本文写明了是哪个测试）。
  - 空白表示还没测。
- 一项失败不要停下，登记后继续测后面的项；个别项有前置条件，已在步骤里写明。
- 不要以管理员（Windows）或 root（Linux/macOS）身份运行被测应用：以管理员身份运行时，系统会阻止从普通权限的资源管理器拖放文件到窗口里，会让 OPEN-04 到 OPEN-06 假失败。
- 预期结果里写到的行为，只要有一处对不上就算失败，不用判断严不严重。拿不准的现象也登记，在“现象”里写清楚。

## 1. 准备

### 1.1 被测版本与环境记录

先把每台机器的信息填好。下表的列与第 2 章总表一致。

| 项目 | Win10 1809 | Win11 | macOS 15 arm64 | macOS 15 Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|-----------|-------|----------------|----------------|--------------|-----------|-----------|
| 测试日期 | | | | | | | |
| 测试人 | | | | | | | |
| 真机或虚拟机（虚拟机写软件名） | | | | | | | |
| 系统版本（含构建号） | | | | | | | |
| 桌面环境与会话（Linux 写 GNOME 版本，X11 或 Wayland） | N/A | N/A | N/A | N/A | | | |
| 安装包文件名 | | | | | | | |
| 绿色版 zip 文件名与 SHA-256 是否一致（只测 Windows） | | | N/A | N/A | N/A | N/A | N/A |
| 安装包 SHA-256 与 `SHA256SUMS.txt` 是否一致 | | | | | | | |
| 应用版本（关于对话框里的 Version） | | | | | | | |
| 安装前是否确认干净（Win10：无 .NET、无 WebView2） | | N/A | N/A | N/A | N/A | N/A | N/A |

被测发布：GitHub 仓库 `real-zony/EmailFileViewer` 里标签为 `v0.1.0-test.1` 的草稿预发布（pre-release）Release。所有机器必须测同一个标签。

安装包文件名里的 `<版本>` 取自 `src-tauri/tauri.conf.json` 的 `version`（写本文时是 `0.1.0`），不一定和标签一致，以 Release 页面上的实际文件名为准。

### 1.2 获取安装包

1. 用对仓库有权限的 GitHub 账号登录，打开 `https://github.com/real-zony/EmailFileViewer/releases`，找到标签 `v0.1.0-test.1` 的草稿预发布。草稿只有对仓库有权限的账号才看得到，没有权限的账号看不到它。
2. 下载本机对应的安装包和 `SHA256SUMS.txt`，放进同一个文件夹：

   | 平台 | 安装包 |
   |------|--------|
   | Windows 10 1809、Windows 11 | `EmailFileViewer_<版本>_x64-setup.exe`，另外下载绿色版 `EmailFileViewer_<版本>_x64-portable.zip`（PORT 系列用） |
   | macOS 15 Apple Silicon | `EmailFileViewer_<版本>_aarch64.dmg` |
   | macOS 15 Intel | `EmailFileViewer_<版本>_x64.dmg` |
   | Ubuntu 22.04、Debian 13 | `EmailFileViewer_<版本>_amd64.deb` |
   | Fedora 43 | `EmailFileViewer_<版本>_amd64.AppImage` |

3. **请在测试机里用浏览器下载**，不要在宿主机下载再用共享文件夹拷进去。浏览器下载会给文件加上“来自网络”的标记，未签名包的 SmartScreen 和 Gatekeeper 拦截只有带标记时才出现，1.4 的放行步骤才有意义。
   - 如果只能拷贝进去，补上这个标记：

     Windows（PowerShell）：

     ```powershell
     Set-Content -Path .\<安装包> -Stream Zone.Identifier -Value "[ZoneTransfer]`r`nZoneId=3"
     ```

     macOS：

     ```sh
     xattr -w com.apple.quarantine "0083;$(printf '%x' $(date +%s));Safari;" <安装包>
     ```
4. `gh release download` 通常找不到草稿 Release，不要依赖它，用浏览器。

### 1.3 校验 SHA-256

在放着安装包和 `SHA256SUMS.txt` 的文件夹里执行（来自 `docs/release.md`）。校验不通过就停下，不要安装，把现象登记到第 6 章。

Linux：

```sh
sha256sum --check --ignore-missing SHA256SUMS.txt
```

macOS：

```sh
shasum -a 256 --check --ignore-missing SHA256SUMS.txt
```

旧 macOS 的 `shasum` 没有 `--ignore-missing` 时，只算自己下载的那一个：`shasum -a 256 EmailFileViewer_<版本>_aarch64.dmg`，再到 `SHA256SUMS.txt` 里找同样的值。

Windows（PowerShell）。`Get-FileHash` 输出大写，文件里是小写，比较时不区分大小写：

```powershell
Get-FileHash .\EmailFileViewer_<版本>_x64-setup.exe -Algorithm SHA256
Select-String -Path .\SHA256SUMS.txt -Pattern 'x64-setup.exe'
```

`OK`（或两个相同的哈希）说明文件和 CI 从标签构建出来的完全一致。结果记入 1.1，并对应检查项 `DIST-06`。

### 1.4 安装，以及未签名包的放行

当前的构建没有正式签名（9.4、9.5 还没做）：macOS 是 ad-hoc 签名且未公证，Windows 完全没签名。下面的拦截提示都是预期的，放行后继续测。

**Windows 10 1809 和 Windows 11**

1. 开始安装前，Windows 10 1809 必须是干净系统：不装 .NET，不装 WebView2。确认方法在 `DIST-02`。建议先做虚拟机快照。
2. 双击 `EmailFileViewer_<版本>_x64-setup.exe`。
3. 出现“Windows 已保护你的电脑”（Windows protected your PC，SmartScreen）时：点“更多信息”（More info），再点“仍要运行”（Run anyway）。发布者显示“未知发布者”（Unknown publisher），符合未签名构建的预期。
4. 安装过程中如果机器缺少 WebView2，安装程序会用内置的引导程序下载并安装，**此时机器必须能上网**（`docs/release.md`）。
5. 默认按当前用户安装，不需要管理员权限，路径通常是 `%LOCALAPPDATA%\EmailFileViewer`。以安装程序实际显示的路径为准，记下来，UNINST-01 要用。
6. 从开始菜单启动 EmailFileViewer。

**Windows 绿色版（`EmailFileViewer_<版本>_x64-portable.zip`）**

不需要安装。要求：机器上已经有 WebView2 运行时（缺少时的表现是 PORT-06 要测的内容）。

1. 用浏览器下载 zip（带“来自网络”标记），在资源管理器里右键 → “全部提取”，解压到一个有写权限的文件夹，例如 `D:\Tools\EmailFileViewer-portable`。**不要**在 zip 里直接双击运行。
2. 双击 `EmailFileViewer.exe`，SmartScreen 拦截时同上放行。
3. 数据在 exe 旁边的 `data` 文件夹里（1.6）；不要和安装版混用同一个用户的状态来判断结果，PORT 系列的前置条件在每一项里写明。

**macOS 15（Apple Silicon 和 Intel）**

1. 打开 `.dmg`，把 `EmailFileViewer` 拖进“应用程序”（Applications）。
2. 双击启动。macOS 提示无法验证开发者：点“完成”（Done），不要点“移到废纸篓”。
3. 打开“系统设置 → 隐私与安全性”，滚到“安全性”一节，点“仍要打开”（Open Anyway）旁边写着 EmailFileViewer 被阻止的那一条，输入密码或指纹，再点“打开”。只需做一次。macOS 15 上“按住 Control 点击 → 打开”已经不再有效。
4. 不想进设置页时，可以用终端去掉隔离标记：

   ```sh
   xattr -dr com.apple.quarantine /Applications/EmailFileViewer.app
   ```

5. 如果提示“已损坏，无法打开”（damaged），说明这个包完全没有签名，上面的办法没有用：登记为失败，这台机器的后续项无法继续。

**Ubuntu 22.04 和 Debian 13（deb）**

```sh
# 不要只用 dpkg -i，它不会补依赖
sudo apt install ./EmailFileViewer_<版本>_amd64.deb
```

从应用列表启动“EmailFileViewer”，或在终端执行 `emailfileviewer`。（可执行文件名以 `dpkg -L <包名>` 为准，包名用 `dpkg-deb -f <deb 文件> Package` 查。）

**Fedora 43（AppImage）**

```sh
chmod +x EmailFileViewer_<版本>_amd64.AppImage
./EmailFileViewer_<版本>_amd64.AppImage
```

- AppImage 需要 FUSE 2（`libfuse.so.2`）。启动报 `dlopen(): error loading libfuse.so.2` 时：执行 `sudo dnf install fuse-libs`（包名不对时用 `dnf provides '*/libfuse.so.2'` 查），或者不用 FUSE：`./EmailFileViewer_<版本>_amd64.AppImage --appimage-extract-and-run`。出现这种情况都在第 6 章登记（它决定发布说明里要不要写依赖）。
- **AppImage 不会自己注册文件关联。** OPEN-07、OPEN-09、OPEN-10 需要先手工做桌面集成，否则这三项记 N/A 并说明：

  ```sh
  mkdir -p ~/Applications ~/.local/share/applications
  cp EmailFileViewer_<版本>_amd64.AppImage ~/Applications/
  cat > ~/.local/share/applications/emailfileviewer-appimage.desktop <<'EOF'
  [Desktop Entry]
  Type=Application
  Name=EmailFileViewer (AppImage)
  Exec=/home/<你的用户名>/Applications/EmailFileViewer_<版本>_amd64.AppImage %F
  Terminal=false
  Categories=Office;
  MimeType=message/rfc822;application/vnd.ms-outlook;
  EOF
  update-desktop-database ~/.local/share/applications
  xdg-mime default emailfileviewer-appimage.desktop message/rfc822 application/vnd.ms-outlook
  ```

- Ubuntu 22.04 上也可以顺便试一次 AppImage（需要 `sudo apt install libfuse2`），不是必测项。

**虚拟机里窗口一片空白或白屏**：不算通过。先登记现象，再用 `WEBKIT_DISABLE_DMABUF_RENDERER=1 emailfileviewer`（AppImage 同理在命令前加这个变量）重试一次，把重试结果也写进“现象”。

### 1.5 准备样本

样本都在仓库的 `samples/` 目录，**测试机上的字节必须和仓库一致**：S/MIME 签名邮件的签名依赖原始字节，附件“与原始字节一致”的比较也依赖它，换行被转换就会假失败。

- 方式 A（推荐）：在测试机上下载标签对应的源码压缩包 `https://github.com/real-zony/EmailFileViewer/archive/refs/tags/v0.1.0-test.1.zip`，解压后取其中的 `samples/` 目录。压缩包不转换换行。
- 方式 B：用 `git clone`。Windows 上必须关闭换行转换：`git -c core.autocrlf=false clone --branch v0.1.0-test.1 https://github.com/real-zony/EmailFileViewer.git`。（`samples/.gitattributes` 已经把目录标成 `-text`，这里是双保险。）
- 方式 C：从开发机拷贝，用清单校验。开发机上在仓库根目录生成清单：

  ```sh
  git ls-files samples | grep -v -E '^samples/(generator/|licenses/|\.gitattributes$)' | sort | xargs shasum -a 256 > samples.sha256
  ```

  把 `samples.sha256` 和 `samples/` 一起拷到测试机，在两者所在的目录里校验。Linux 用 `sha256sum -c samples.sha256`，macOS 用 `shasum -a 256 -c samples.sha256`，Windows 用：

  ```powershell
  Get-Content .\samples.sha256 | ForEach-Object {
    $hash, $name = $_ -split '\s+', 2
    $name = $name.TrimStart('*', ' ')
    $actual = (Get-FileHash -Algorithm SHA256 -LiteralPath $name).Hash.ToLower()
    if ($actual -ne $hash) { "MISMATCH: $name" }
  }
  ```

目录约定（后文的命令都以此为准，路径自行替换）：

- 样本：`<工作目录>/samples/`，其中 `samples/*.eml`、`samples/*.msg` 共 50 个，`samples/invalid/` 5 个，`samples/assets/` 是营销邮件用的素材。
- 保存附件的输出目录：`~/efv-out/`（Windows 写成 `$HOME\efv-out`），自己创建。
- 有一个文件不在仓库里，需要手工创建：`samples/efv-unreadable.eml`（OPEN-16 里创建）。

样本里有各种语言的文件，系统需要有对应字体才能看到文字。Linux 虚拟机如果缺 CJK 字体，会把汉字显示成方框，这是系统缺字体，不是应用的问题；请先装 `fonts-noto-cjk`（Debian、Ubuntu）或 `google-noto-sans-cjk-fonts`（Fedora）。

### 1.6 重置应用设置（“首次启动”状态）

LOC-01、LOC-02、APPR-01、START-01 要在没有保存过设置的状态下首次启动。先**完全退出应用**，再删除设置文件：

- Windows（PowerShell）：`Remove-Item "$env:APPDATA\com.github.realzony.emailfileviewer\settings.json" -ErrorAction SilentlyContinue`
- Windows 绿色版：删除 exe 旁边的 `data\settings.json`（或整个 `data` 文件夹）。
- macOS：`rm -f ~/Library/Application\ Support/com.github.realzony.emailfileviewer/settings.json`
- Linux：`rm -f "${XDG_DATA_HOME:-$HOME/.local/share}/com.github.realzony.emailfileviewer/settings.json"`

找不到这个文件时，说明还没保存过设置（只有改过设置才会写文件），也就已经是首次启动状态。想确认应用到底用哪个目录，可以在改过一次设置后用 `find`（macOS、Linux）或 `Get-ChildItem -Recurse -Filter settings.json`（Windows）搜一下。

### 1.7 常用查看方法

**进程名**（以安装后实际的文件名为准）：

| 进程 | Windows | macOS、Linux |
|------|---------|--------------|
| 主程序 | `emailfileviewer.exe` | `emailfileviewer` |
| 解析进程（sidecar） | `efv-sidecar.exe` | `efv-sidecar` |
| WebView | `msedgewebview2.exe`（多个） | macOS：`com.apple.WebKit.WebContent`、`com.apple.WebKit.Networking`；Linux：`WebKitWebProcess`、`WebKitNetworkProcess` |

sidecar 在第一次打开文件时才启动，空状态下没有它。

**数主程序进程**（预期都是 1）：

- Windows：`(Get-Process emailfileviewer -ErrorAction SilentlyContinue | Measure-Object).Count`
- macOS、Linux：`pgrep -x emailfileviewer | wc -l`

**确认应用已完全退出**：上面的命令输出 0，且 `efv-sidecar` 也不在了（Windows：`Get-Process efv-sidecar`；macOS、Linux：`pgrep -x efv-sidecar`）。macOS 用 ⌘Q 退出，Windows 和 Linux 关闭窗口即可。

**数窗口**：macOS 看 Dock 图标的右键菜单或 Mission Control；Windows 看任务栏缩略图或 Alt+Tab；Linux 看活动概览（Activities）。

**切换系统深色模式**：

- Windows：设置 → 个性化 → 颜色 → 选择模式（Windows 10 1809 是“选择默认应用模式”）。
- macOS：系统设置 → 外观 → 浅色、深色。
- Linux（GNOME）：设置 → 外观 → 样式；或 `gsettings set org.gnome.desktop.interface color-scheme 'prefer-dark'` 与 `'default'`。

**切换时区**（改完需要重启应用）：

- Windows（管理员 PowerShell）：`tzutil /s "China Standard Time"`（UTC+8），`tzutil /s "Pacific Standard Time"`（10 月是 UTC-7）。
- macOS：系统设置 → 通用 → 日期与时间，关闭“自动设定时区”，选择 Shanghai 或 Los Angeles。
- Linux：`sudo timedatectl set-timezone Asia/Shanghai`，`sudo timedatectl set-timezone America/Los_Angeles`。

**文件哈希**：Windows 用 `Get-FileHash <文件> -Algorithm SHA256`，macOS 用 `shasum -a 256 <文件>`，Linux 用 `sha256sum <文件>`。

### 1.8 建议的执行顺序

同一台机器上，建议按下面的顺序做，前面的项会影响后面的状态：

1. 环境记录（1.1）、下载、校验（DIST-06）、安装（1.4）。
2. **先做依赖“首次启动”的项**：START-01、LOC-01、LOC-02、APPR-01。每做完一项，按 1.6 重置设置。
3. DIST-01 到 DIST-05、DIST-07、DIST-08。
   - Windows 上另外做 PORT 系列：**PORT-06 要在装好 WebView2 之前做**（Win10 1809 的干净快照），其余 PORT-01 到 PORT-05、PORT-08 在没有安装版的状态下做，PORT-07 需要安装版和绿色版同时存在，最后做。
4. 第 3 章的 OPEN、TAB、KEY、TITLE、VIEW、LINK、SMIME、ATT、LOC-03、LOC-04、APPR-02 之后各项。
5. 第 4 章。抓包的几项（SEC-07、SEC-08）单独做，期间不要点链接，也不要做别的需要联网的事。
6. **最后做第 5 章卸载**，做之前给虚拟机打快照。

## 2. 平台矩阵总表

预填说明：`N/A` 是结构性不适用，`CI` 是由自动化测试覆盖。编号的完整含义见第 3 到 5 章。

**3.1 安装、启动与分发（app-distribution）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| DIST-01 | 在各最低版本上安装并启动，打开一封示例邮件 |  |  |  |  |  |  |  |
| DIST-02 | 全新的 Windows 10 上安装，不需要手动装 .NET 和 WebView2 |  | N/A | N/A | N/A | N/A | N/A | N/A |
| DIST-03 | deb 的依赖由包管理器自动安装 | N/A | N/A | N/A | N/A |  |  | N/A |
| DIST-04 | AppImage 不需要先装 WebKitGTK 和 .NET | N/A | N/A | N/A | N/A | N/A | N/A |  |
| DIST-05 | 发布页上有每个平台的安装包和校验文件 | N/A |  | N/A | N/A | N/A | N/A | N/A |
| DIST-06 | 安装包的 SHA-256 与发布页一致 |  |  |  |  |  |  |  |
| DIST-07 | macOS 签名与公证（9.4 完成后才有意义） | N/A | N/A |  |  | N/A | N/A | N/A |
| DIST-08 | Windows 代码签名（9.5 完成后才有意义） |  |  | N/A | N/A | N/A | N/A | N/A |
| START-01 | 首次启动时窗口没有闪白 |  |  |  |  |  |  |  |
| PORT-01 | 绿色版：解压即用，能打开、查看邮件并保存附件 |  |  | N/A | N/A | N/A | N/A | N/A |
| PORT-02 | 绿色版：不留下注册表项和 `.eml` / `.msg` 文件关联 |  |  | N/A | N/A | N/A | N/A | N/A |
| PORT-03 | 绿色版：数据在程序旁的 `data` 里，整个文件夹复制到另一台电脑后设置保留 |  |  | N/A | N/A | N/A | N/A | N/A |
| PORT-04 | 绿色版：删除整个文件夹清除全部持久数据 |  |  | N/A | N/A | N/A | N/A | N/A |
| PORT-05 | 绿色版：程序目录不可写时退回标准目录并照常运行 |  |  | N/A | N/A | N/A | N/A | N/A |
| PORT-06 | 缺少 WebView2 时弹出系统对话框（Win11 用环境变量模拟） |  |  | N/A | N/A | N/A | N/A | N/A |
| PORT-07 | 绿色版与安装版同时运行：后启动的把文件交给先启动的 |  |  | N/A | N/A | N/A | N/A | N/A |
| PORT-08 | 绿色版 zip 的内容正确，二进制与安装版相同 |  |  | N/A | N/A | N/A | N/A | N/A |

**3.2 打开文件（email-opening）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| OPEN-01 | 通过文件对话框打开单个 .msg 文件 |  |  |  |  |  |  |  |
| OPEN-02 | 取消文件对话框 |  |  |  |  |  |  |  |
| OPEN-03 | 在对话框里一次选多个文件 |  |  |  |  |  |  |  |
| OPEN-04 | 拖入一个 EML 文件 |  |  |  |  |  |  |  |
| OPEN-05 | 真实拖放：一次拖入多个文件 |  |  |  |  |  |  |  |
| OPEN-06 | 真实拖放：拖入文本和链接 |  |  |  |  |  |  |  |
| OPEN-07 | 双击 .msg 文件启动应用 |  |  |  |  |  |  |  |
| OPEN-08 | 命令行传入路径 |  |  |  |  |  |  |  |
| OPEN-09 | “打开方式”列表里有本应用，.eml 和 .msg 都有 |  |  |  |  |  |  |  |
| OPEN-10 | 应用运行中再双击另一个文件：已有实例处理，只有一个进程 |  |  |  |  |  |  |  |
| OPEN-11 | 应用运行中再从命令行启动：转发给已有实例 |  |  |  |  |  |  |  |
| OPEN-12 | 扩展名与内容不符 |  |  |  |  |  |  |  |
| OPEN-13 | 打开图片等不是邮件的文件 |  |  |  |  |  |  |  |
| OPEN-14 | 解析异常的文件：损坏的 MSG，以及 sidecar 进程被杀掉 |  |  |  |  |  |  |  |
| OPEN-15 | 一次拖入三个文件，其中一个已损坏 |  |  |  |  |  |  |  |
| OPEN-16 | 无法读取的文件 |  |  |  |  |  |  |  |

**3.3 标签页、快捷键与窗口标题（任务 5.6、7.10）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| TAB-01 | 标签栏的显示规则与基本操作 |  |  |  |  |  |  |  |
| KEY-01 | macOS：⌘O 打开文件 | N/A | N/A |  |  | N/A | N/A | N/A |
| KEY-02 | macOS：⌘W 关闭当前标签页，焦点在正文以外 | N/A | N/A |  |  | N/A | N/A | N/A |
| KEY-03 | macOS：焦点在正文里时 ⌘W 仍能关闭标签页 | N/A | N/A |  |  | N/A | N/A | N/A |
| KEY-04 | macOS：⌘, 打开设置 | N/A | N/A |  |  | N/A | N/A | N/A |
| KEY-05 | macOS：菜单栏内容与复制 | N/A | N/A |  |  | N/A | N/A | N/A |
| KEY-06 | Windows 和 Linux：没有菜单栏 |  |  | N/A | N/A |  |  |  |
| KEY-07 | Windows 和 Linux：Ctrl+O 打开文件（焦点在正文以外） |  |  | N/A | N/A |  |  |  |
| KEY-08 | Windows 和 Linux：Ctrl+W 关闭当前标签页（焦点在正文以外） |  |  | N/A | N/A |  |  |  |
| KEY-09 | Windows 和 Linux：焦点在正文里时的已知限制 |  |  | N/A | N/A |  |  |  |
| TITLE-01 | 窗口标题跟随当前邮件 |  |  |  |  |  |  |  |

**3.4 邮件显示（email-viewing）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| VIEW-01 | 普通邮件：显示邮件头 |  |  |  |  |  |  |  |
| VIEW-02 | 显示密送和回复地址；没有抄送时不出现空栏 |  |  |  |  |  |  |  |
| VIEW-03 | 日期按本地时区显示 |  |  |  |  |  |  |  |
| VIEW-04 | 同时有 HTML 和纯文本时显示 HTML |  |  |  |  |  |  |  |
| VIEW-05 | 只有 RTF 正文的 MSG |  |  |  |  |  |  |  |
| VIEW-06 | 纯文本保留换行 |  |  |  |  |  |  |  |
| VIEW-07 | GBK 编码的中文邮件 |  |  |  |  |  |  |  |
| VIEW-08 | 未声明字符集（Big5） |  |  |  |  |  |  |  |
| VIEW-09 | 其他字符集与 RFC 2047 邮件头 |  |  |  |  |  |  |  |
| VIEW-10 | 分段正文（Apple Mail 式）：图片夹在文字之间 |  |  |  |  |  |  |  |
| VIEW-11 | 内嵌图片（cid）显示在正文对应位置 |  |  |  |  |  |  |  |
| VIEW-12 | 其余样本逐个打开 |  |  |  |  |  |  |  |

**3.5 正文链接（email-viewing / 正文链接在系统浏览器中打开）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| LINK-01 | 点击正文里的 https 链接：系统浏览器打开，应用里没有新窗口 |  |  |  |  |  |  |  |
| LINK-02 | 链接文字与实际地址不符：悬停显示实际地址 |  |  |  |  |  |  |  |
| LINK-03 | 正文里的页内锚点：能滚动，点击后正文不消失 |  |  |  |  |  |  |  |
| LINK-04 | 正文里的右键菜单（观察项） |  |  |  |  |  |  |  |

**3.6 加密与签名邮件（email-viewing / 加密与签名邮件提示）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| SMIME-01 | 加密邮件 |  |  |  |  |  |  |  |
| SMIME-02 | 签名邮件 |  |  |  |  |  |  |  |

**3.7 附件（email-attachments）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| ATT-01 | 列出附件：PDF 和 xlsx |  |  |  |  |  |  |  |
| ATT-02 | 内嵌图片不重复列出 |  |  |  |  |  |  |  |
| ATT-03 | 保存单个附件，内容与原字节一致 |  |  |  |  |  |  |  |
| ATT-04 | 保存全部：目标文件夹已有同名文件时不覆盖 |  |  |  |  |  |  |  |
| ATT-05 | 附件文件名的安全处理（路径穿越、保留名、非法字符） |  |  |  |  |  |  |  |
| ATT-06 | 用系统默认程序打开附件 |  |  |  |  |  |  |  |
| ATT-07 | 附件的“来自网络”标记 |  |  |  |  | N/A | N/A | N/A |
| ATT-08 | 可执行和脚本类附件只能保存，不能打开 |  |  |  |  |  |  |  |
| ATT-09 | 打开嵌套邮件：MSG 中附带另一封 MSG |  |  |  |  |  |  |  |
| ATT-10 | 多层嵌套：继续往里打开 |  |  |  |  |  |  |  |
| ATT-11 | EML 里的嵌套邮件，以及作为附件的 .eml 和 .msg |  |  |  |  |  |  |  |
| ATT-12 | 展开 winmail.dat |  |  |  |  |  |  |  |

**3.8 界面语言（app-localization）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| LOC-01 | 系统语言为简体中文：首次启动显示简体中文 |  |  |  |  |  |  |  |
| LOC-02 | 系统语言为德语：首次启动显示英文 |  |  |  |  |  |  |  |
| LOC-03 | 手动切换语言：立即生效，重启后保留 |  |  |  |  |  |  |  |
| LOC-04 | 界面语言不改变邮件内容 |  |  |  |  |  |  |  |

**3.9 外观（app-appearance）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| APPR-01 | 首次启动跟随系统：系统是深色时界面是深色 |  |  |  |  |  |  |  |
| APPR-02 | 运行中系统切换外观：界面立即跟随 |  |  |  |  |  |  |  |
| APPR-03 | 手动固定为浅色（或深色）：不再受系统影响 |  |  |  |  |  |  |  |
| APPR-04 | 切换配色方案：立即生效，明暗模式不变 |  |  |  |  |  |  |  |
| APPR-05 | 配色方案与明暗模式组合：每套都有明暗两种变体，正文始终是浅色底 |  |  |  |  |  |  |  |
| APPR-06 | 重启后保持外观设置 |  |  |  |  |  |  |  |
| APPR-07 | 所有配色的可读性（对比度） | CI | CI | CI | CI | CI | CI | CI |
| APPR-08 | 深色模式下查看 HTML 邮件：正文仍按邮件自身样式显示在浅色底上 |  |  |  |  |  |  |  |
| APPR-09 | 深色模式下查看纯文本邮件：深色文字在浅色底上 |  |  |  |  |  |  |  |

**4.3 恶意 HTML 样本**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| SEC-01 | 恶意 HTML：脚本都不执行，正文其余部分正常显示 |  |  |  |  |  |  |  |
| SEC-02 | 恶意 HTML：调用不到应用的内部接口，应用状态和用户文件不受影响 |  |  |  |  |  |  |  |
| SEC-03 | 放开远程内容之后，脚本仍然不执行 |  |  |  |  |  |  |  |

**4.4 远程内容**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| SEC-04 | 远程内容默认不加载 |  |  |  |  |  |  |  |
| SEC-05 | 用户选择加载：只对当前这封邮件生效 |  |  |  |  |  |  |  |

**4.5 断网与抓包**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| SEC-06 | 断网状态下打开全部样本 |  |  |  |  |  |  |  |
| SEC-07 | 联网抓包：未允许远程内容时，应用没有任何网络请求（包括 DNS） |  |  |  |  |  |  |  |
| SEC-08 | 点“加载远程内容”之后才出现请求，而且只对当前这封邮件生效 |  |  |  |  |  |  |  |

**4.6 Windows 和 Linux 上的链接、锚点、新窗口拦截（任务 5.7 的实验）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| SEC-09 | http、mailto 以及 SVG 和图像映射里的外部链接交给系统，应用里没有新窗口 |  |  |  |  |  |  |  |
| SEC-10 | tel 和 file 链接不起作用 |  |  |  |  |  |  |  |
| SEC-11 | 相对链接和空链接不会毁掉正文 |  |  |  |  |  |  |  |

**5. 安装与卸载（9.6）**

| 编号 | 检查项 | Win10 1809 | Win11 | macOS arm64 | macOS Intel | Ubuntu 22.04 | Debian 13 | Fedora 43 |
|------|------|------|------|------|------|------|------|------|
| UNINST-01 | Windows 卸载：应用文件被删除，文件关联被移除 |  |  | N/A | N/A | N/A | N/A | N/A |
| UNINST-02 | deb 卸载：应用文件被删除，文件关联被移除 | N/A | N/A | N/A | N/A |  |  | N/A |


## 3. 功能验收（10.1）

对应 tasks.md 10.1：在各最低平台上安装正式构建的安装包，用样本逐条核对六个规格里的场景。

### 3.1 安装、启动与分发（app-distribution）

#### DIST-01 在各最低版本上安装并启动，打开一封示例邮件
- **规格**：app-distribution / 支持的平台 / 在各最低版本上运行
- **样本**：`samples/headers-normal.eml`、`samples/body-rtf-only.msg`
- **步骤**：
  1. 按 1.4 装好应用，从开始菜单、启动台或应用列表启动。
  2. 看到空状态后，点 “Open file…”（“打开文件…”），选 `samples/headers-normal.eml`。
  3. 再用同样的办法打开 `samples/body-rtf-only.msg`（.msg 会走 sidecar 的 MSG 解析路径，能同时验证 sidecar 在这个平台上能启动）。
  4. 退出应用，用 1.7 的方法确认主程序和 `efv-sidecar` 都已消失。
- **预期**：应用正常启动，没有错误对话框，窗口能正常缩放；空状态显示标题 “Open an email file”（“打开邮件文件”）和隐私说明；两封邮件都正确显示（EML 的主题是 `Quarterly planning meeting`；MSG 的正文含 `RTF-ONLY-BODY`）；退出后没有残留进程。

#### DIST-02 全新的 Windows 10 上安装，不需要手动装 .NET 和 WebView2
- **规格**：app-distribution / 无需额外安装运行时 / 全新的 Windows 10
- **样本**：`samples/headers-normal.eml`、`samples/body-rtf-only.msg`
- **步骤**：
  1. 在安装前，打开 PowerShell 确认机器是干净的，并把结果记入 1.1：

     ```powershell
     dotnet --list-runtimes
     Get-ItemProperty 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}' -ErrorAction SilentlyContinue
     Get-ItemProperty 'HKCU:\Software\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}' -ErrorAction SilentlyContinue
     ```

     第一条应报“无法识别 dotnet”，后两条应没有任何输出；“设置 → 应用”里也不应有 Microsoft Edge WebView2 Runtime。
  2. 保持联网，按 1.4 运行安装程序，观察安装过程。
  3. 安装完成后，再执行第 1 步的三条命令，并看“设置 → 应用”。
  4. 启动应用，打开 `samples/headers-normal.eml` 和 `samples/body-rtf-only.msg`。
- **预期**：
  - 安装程序自动安装 WebView2，全程没有要求你手动装 .NET、WebView2 或其他运行时。
  - 安装后“应用”列表里出现 Microsoft Edge WebView2 Runtime；`dotnet` 仍然不存在（应用自带解析器，不依赖 .NET）。
  - 应用正常启动并显示两封邮件。
  - 如果启动时报缺少 `VCRUNTIME140.dll`、`MSVCP140.dll` 之类的 C++ 运行库，记为失败：这是干净系统才会暴露的问题。

#### DIST-03 deb 的依赖由包管理器自动安装
- **规格**：app-distribution / 无需额外安装运行时（deb 声明依赖）
- **样本**：`samples/headers-normal.eml`
- **步骤**：
  1. 安装前执行 `dpkg-deb -f EmailFileViewer_<版本>_amd64.deb Depends` 和 `dpkg -l libwebkit2gtk-4.1-0 libgtk-3-0`，记下依赖声明和这两个包安装前是否已存在。
  2. 确认机器上没有 .NET：`dotnet --version` 应报命令不存在。
  3. 执行 `sudo apt install ./EmailFileViewer_<版本>_amd64.deb`，观察 apt 要补装的包。
  4. 启动应用，打开样本。
- **预期**：`Depends` 里有 `libwebkit2gtk-4.1-0` 和 `libgtk-3-0`；apt 自动补装缺少的依赖，没有任何手动步骤；应用启动并显示邮件；机器上始终没有 .NET。

#### DIST-04 AppImage 不需要先装 WebKitGTK 和 .NET
- **规格**：app-distribution / 无需额外安装运行时
- **样本**：`samples/headers-normal.eml`
- **步骤**：
  1. 执行 `dnf list installed 'webkit2gtk*'` 和 `dotnet --version`，记下系统里是否本来就有 WebKitGTK 和 .NET（最好是都没有）。
  2. 按 1.4 运行 AppImage。FUSE 的问题按 1.4 处理并登记。
  3. 打开样本。
- **预期**：应用启动并显示邮件，不需要为它再装 WebKitGTK 或 .NET。

#### DIST-05 发布页上有每个平台的安装包和校验文件
- **规格**：app-distribution / 安装包形态 / 查看发布页；发布产物可追溯
- **样本**：无
- **步骤**：只需在一台机器上做一次，结果填在 Win11 一列。
  1. 打开标签 `v0.1.0-test.1` 的草稿预发布页面，核对附件列表。
  2. 打开仓库的 Actions 页，找到这个标签对应的 `release` 工作流运行。
  3. 在本地执行 `git rev-parse v0.1.0-test.1^{commit}`，与 Release 页面显示的提交对比。
- **预期**：
  - 附件恰好有 6 个安装文件和 `SHA256SUMS.txt`：`EmailFileViewer_<版本>_x64-setup.exe`、`_x64-portable.zip`（Windows 绿色版）、`_aarch64.dmg`、`_x64.dmg`、`_amd64.AppImage`、`_amd64.deb`。
  - `SHA256SUMS.txt` 里有这 6 个文件的哈希。
  - 工作流里 `create-release`、四个 `build` 任务和 `checksums` 任务都是成功状态。
  - Release 对应的提交就是标签指向的提交（安装包由 CI 从带版本标签的提交构建）。

#### DIST-06 安装包的 SHA-256 与发布页一致
- **规格**：app-distribution / 发布产物可追溯 / 校验下载的安装包
- **样本**：无
- **步骤**：按 1.3 在每台机器上校验自己下载的那个安装包（Windows 两台机器上还要校验绿色版的 zip）。
- **预期**：命令输出 `OK`（或哈希完全相同）。

#### DIST-07 macOS 签名与公证（9.4 完成后才有意义）
- **规格**：app-distribution / macOS 签名与公证 / 首次打开下载的应用
- **样本**：无
- **步骤**：
  1. 用浏览器下载 `.dmg`，打开，把应用拖入“应用程序”，**不做** 1.4 的任何放行步骤，直接双击启动。
  2. 执行：

     ```sh
     codesign -dv --verbose=4 /Applications/EmailFileViewer.app
     codesign -dv --verbose=4 /Applications/EmailFileViewer.app/Contents/MacOS/efv-sidecar
     spctl -a -vv /Applications/EmailFileViewer.app
     ```
- **预期**：首次启动不出现“无法验证开发者”或“已损坏”的拦截；两个 `codesign` 都显示 `Authority=Developer ID Application: ...`；`spctl` 输出 `accepted` 且 `source=Notarized Developer ID`。
- **当前构建**：9.4 还没做，构建是 ad-hoc 签名，必然被拦截。此时本项填 `N/A`，备注写“9.4 未完成”，并确认 1.4 的放行流程能走通。

#### DIST-08 Windows 代码签名（9.5 完成后才有意义）
- **规格**：app-distribution / Windows 代码签名 / 运行安装程序
- **样本**：无
- **步骤**：
  1. 在下载的安装程序上右键 → 属性，看有没有“数字签名”选项卡。
  2. 执行 `Get-AuthenticodeSignature .\EmailFileViewer_<版本>_x64-setup.exe | Format-List`。
  3. 双击运行，看 SmartScreen 或权限确认对话框里的发布者。
  4. 安装后对安装目录下的主程序和 `efv-sidecar.exe` 各执行一次 `Get-AuthenticodeSignature`；对绿色版 zip 里的 `EmailFileViewer.exe` 和 `efv-sidecar.exe` 也各执行一次（PORT-08）。
- **预期**：`Status` 是 `Valid`；对话框里显示发布者名称，而不是“未知发布者”；主程序和 sidecar 都有有效签名，绿色版 zip 里的两个 exe 也一样（zip 本身不签名）。
- **当前构建**：9.5 还没做，构建未签名。此时本项填 `N/A`，备注写“9.5 未完成”。

#### START-01 首次启动时窗口没有闪白
- **规格**：无（design.md D7、任务 8.3“在首次绘制前应用设置以避免闪烁”）
- **样本**：无
- **步骤**：
  1. 按 1.6 重置设置，把系统设成深色模式。
  2. 完全退出后重新启动应用，连续做 5 次。最好用屏幕录制（macOS 的“截屏”工具栏、Windows 的 Xbox Game Bar `Win+Alt+R`、Linux 的 GNOME 屏幕录制 `Ctrl+Alt+Shift+R`）逐帧回看，没有录屏就肉眼盯着窗口出现的瞬间。
  3. 把系统改回浅色，在设置里把模式固定为 Dark（“深色”），再启动 5 次。
  4. 把语言设成简体中文，再启动 5 次，留意启动瞬间有没有先出现英文再变成中文。
- **预期**：窗口出现时底色就是正确的颜色：第 2、3 步是深色，没有任何一帧是白色；第 4 步从第一帧起就是中文，没有英文一闪而过。
- **判定**：只要有一次看到白闪或语言闪变，就记为失败，并在“现象”里写明是第几次、什么模式。

#### PORT-01 绿色版解压即用
- **规格**：app-distribution / Windows 绿色版免安装运行 / 解压即用
- **样本**：`samples/headers-normal.eml`、`samples/attachments-pdf-xlsx-jpg.eml`
- **前置**：机器上已经有 WebView2 运行时（Win10 1809 要先装好，但**还没有**装安装版，PORT-06 要在装好 WebView2 之前做）；用普通用户，不要以管理员运行；这个用户从没运行过本应用（最好用虚拟机快照）。
- **步骤**：
  1. 用浏览器下载 `EmailFileViewer_<版本>_x64-portable.zip`（1.2），按 1.3 校验 SHA-256。
  2. 在资源管理器里右键 → “全部提取”（Extract All），解压到一个有写权限的文件夹，例如 `D:\Tools\EmailFileViewer-portable`。记下文件夹里有哪些文件，用记事本打开 `README.txt`。
  3. 双击 `EmailFileViewer.exe`。SmartScreen 拦截时按 1.4 放行。
  4. 打开 `samples/headers-normal.eml`。再打开 `samples/attachments-pdf-xlsx-jpg.eml`：保存其中一个附件到桌面，点“全部保存”（“Save all”）保存到一个空文件夹，再点 PDF 附件的“打开”（“Open”）用默认程序打开。
  5. 把另一个 `.eml` 拖进窗口；再把一个 `.msg` 拖到 `EmailFileViewer.exe` 的图标上。
  6. 退出，按 1.7 确认 `EmailFileViewer.exe` 和 `efv-sidecar.exe` 都已消失。
- **预期**：
  - 解压出来的文件夹里恰好 4 个文件：`EmailFileViewer.exe`、`efv-sidecar.exe`、`EmailFileViewer.portable`、`README.txt`；`README.txt` 在记事本里换行正常。
  - 全程没有安装步骤，没有 UAC（管理员权限）提示；应用正常启动，邮件显示正确（EML 的主题是 `Quarterly planning meeting`）。
  - 附件保存、全部保存、打开都正常；保存出来的文件带“来自网络”标记（`Get-Content -Path <文件> -Stream Zone.Identifier` 能读到 `ZoneId=3`）。
  - 拖进窗口和拖到图标上都能打开邮件。
  - 运行之后，文件夹里多了 `data` 文件夹；退出后没有残留进程。

#### PORT-02 绿色版不留下注册表项和文件关联
- **规格**：app-distribution / Windows 绿色版免安装运行 / 不留下注册表和文件关联
- **样本**：`samples/headers-normal.eml`、`samples/body-rtf-only.msg`
- **前置**：用一个从没运行过本应用的用户账户（或虚拟机快照），并且这台机器上没有安装版。**不要**在这一项里用资源管理器的“打开方式”选择本程序：那是系统自己记录的，会产生注册表项，不属于本应用。
- **步骤**：
  1. 运行之前记录状态：

     ```powershell
     reg export HKCU "$env:TEMP\hkcu-before.reg" /y
     cmd /c assoc .eml
     cmd /c assoc .msg
     Get-ItemProperty 'HKCU:\Software\Classes\.eml','HKCU:\Software\Classes\.msg' -ErrorAction SilentlyContinue
     Get-ItemProperty 'HKLM:\SOFTWARE\Classes\.eml','HKLM:\SOFTWARE\Classes\.msg' -ErrorAction SilentlyContinue
     ```

     再看“设置 → 默认应用 → 按文件类型选择默认应用”里 `.eml`、`.msg` 的默认程序，截图。
  2. 运行绿色版，打开 `samples/headers-normal.eml` 和 `samples/body-rtf-only.msg`，保存一个附件，把语言改成简体中文，退出。
  3. 再导出一次并搜索：

     ```powershell
     reg export HKCU "$env:TEMP\hkcu-after.reg" /y
     Select-String -Path "$env:TEMP\hkcu-after.reg" -Pattern 'EmailFileViewer','efv-sidecar','realzony' | Select-Object -First 20
     Compare-Object (Get-Content "$env:TEMP\hkcu-before.reg") (Get-Content "$env:TEMP\hkcu-after.reg") | Measure-Object
     cmd /c assoc .eml
     cmd /c assoc .msg
     ```
  4. 再看一遍默认应用页面、开始菜单（搜索 EmailFileViewer）和“设置 → 应用 → 已安装的应用”。
- **预期**：
  - 第 3 步的搜索没有任何结果（没有名字或数据里含 `EmailFileViewer`、`efv-sidecar`、`realzony` 的键或值）；`assoc` 和 `HKCU`/`HKLM` 的 `.eml`、`.msg` 与运行前完全相同；默认应用页面里的默认程序没有变化。
  - 开始菜单里没有快捷方式，已安装的应用列表里没有本应用。
  - 差异里如果出现 Windows 自己记录“程序运行过”的键（例如 `FeatureUsage`、`UserAssist`、`AppCompatFlags` 下的），或 WebView2 运行时自己的键，不是本应用写的，不算失败，把键路径写进“现象”。

#### PORT-03 绿色版的数据放在程序旁边，复制到另一台电脑后设置保留
- **规格**：app-distribution / Windows 绿色版的数据位置 / 复制到另一台电脑
- **样本**：`samples/headers-normal.eml`
- **前置**：两台 Windows（第二台可以是另一台虚拟机），都有 WebView2，都没有运行过本应用。
- **步骤**：
  1. 第一台：把绿色版解压到一个有写权限的文件夹，确认里面还没有 `data`。启动，打开设置：模式选 Dark（“深色”），语言选简体中文，配色选一个非默认的（例如 Plum）。退出。
  2. 查看文件夹：

     ```powershell
     Get-Content .\data\settings.json
     Get-ChildItem .\data -Recurse -Depth 1 | Select-Object FullName
     ```

  3. 查看标准目录：`%APPDATA%\com.github.realzony.emailfileviewer` 和 `%LOCALAPPDATA%\com.github.realzony.emailfileviewer`（`Test-Path` 即可）。
  4. 把**整个文件夹**（含 `data`）复制到第二台电脑的本地磁盘（U 盘、共享文件夹都行），在第二台上启动，盯着窗口出现的瞬间（做法同 START-01）。
- **预期**：
  - `data\settings.json` 含 `"language": "zh-CN"`、`"mode": "dark"` 和所选的配色；`data\webview\` 存在且有内容；`data\logs\` 存在。
  - 标准目录不存在，或者里面没有因为这次运行而新增的内容。
  - 第二台启动时，从第一帧起就是深色、简体中文和所选配色，没有闪白、没有英文一闪而过；第二台的标准目录里同样没有写入设置。

#### PORT-04 删除整个文件夹清除全部持久数据
- **规格**：app-distribution / Windows 绿色版的数据位置（删除整个文件夹）
- **样本**：`samples/attachments-pdf-xlsx-jpg.eml`
- **前置**：在 PORT-03 的第一台电脑上继续做，这台机器上没有安装版。
- **步骤**：
  1. 启动绿色版，打开样本，点 PDF 附件的“打开”（会往系统临时目录写文件），关闭 PDF 阅读器，然后退出应用。
  2. 看系统临时目录：`Get-ChildItem "$env:TEMP\EmailFileViewer" -Recurse -ErrorAction SilentlyContinue`。
  3. 删除整个绿色版文件夹。
  4. 搜索残留：

     ```powershell
     Get-ChildItem $env:APPDATA, $env:LOCALAPPDATA -Directory -Filter '*realzony*' -ErrorAction SilentlyContinue
     Get-ChildItem $env:APPDATA, $env:LOCALAPPDATA -Directory -Filter '*EmailFileViewer*' -ErrorAction SilentlyContinue
     reg export HKCU "$env:TEMP\hkcu-after-delete.reg" /y; Select-String -Path "$env:TEMP\hkcu-after-delete.reg" -Pattern 'EmailFileViewer','realzony'
     ```

  5. 重新解压同一个 zip，启动。
- **预期**：
  - 第 2 步：退出之后系统临时目录里没有残留的会话文件夹或提取出来的附件（按现有规则在退出时清理）。
  - 第 4 步：三条命令都没有输出：删除文件夹之后，没有任何本应用的持久数据留在系统里。
  - 第 5 步：回到首次启动状态（语言跟随系统，模式跟随系统，默认配色）。

#### PORT-05 程序目录不可写时退回标准目录
- **规格**：app-distribution / Windows 绿色版的数据位置 / 程序目录不可写
- **样本**：`samples/headers-normal.eml`
- **步骤**：
  1. 以管理员身份把解压好的绿色版文件夹整个复制到 `C:\Program Files\EmailFileViewer-portable`（普通用户对 `Program Files` 只有读权限）。
  2. 以**普通用户**（不是管理员）双击其中的 `EmailFileViewer.exe`。
  3. 打开样本，把语言改成简体中文，退出，再启动一次。
  4. 检查：

     ```powershell
     Test-Path 'C:\Program Files\EmailFileViewer-portable\data'
     Get-Content "$env:APPDATA\com.github.realzony.emailfileviewer\settings.json"
     Select-String -Path "$env:LOCALAPPDATA\com.github.realzony.emailfileviewer\logs\*.log" -Pattern 'not writable'
     ```

  5. 可选的第二种情形：在一个可写文件夹里放好绿色版，先在旁边建一个名叫 `data` 的**普通文件**（不是文件夹），再启动。
- **预期**：
  - 应用正常启动，没有错误对话框；邮件能打开；重启后语言仍是简体中文。
  - 第 4 步：`Test-Path` 返回 `False`（程序目录里没有新增 `data`）；设置在 `%APPDATA%\com.github.realzony.emailfileviewer\settings.json` 里；日志里有一条 WARN，内容是 `the portable data folder next to the program is not writable (...)`。
  - 第 5 步的情形同样正常启动，设置落在标准目录。

#### PORT-06 缺少 WebView2 时的提示
- **规格**：app-distribution / 缺少 WebView2 时的提示 / 在没有 WebView2 的系统上运行绿色版
- **样本**：无
- **步骤**：
  1. Win10 1809 的干净快照（按 DIST-02 第 1 步确认没有 WebView2，也没有装安装版）。解压绿色版，双击 `EmailFileViewer.exe`。
  2. 阅读对话框，按 `Ctrl+C`，把复制到的文字粘贴进记事本，再点“确定”。
  3. 查看任务管理器里有没有 `emailfileviewer.exe`、`efv-sidecar.exe`，以及 exe 旁边有没有新增 `data` 文件夹。
  4. 把 Windows 显示语言改成“中文(简体)”（设置 → 时间和语言 → 语言，需要注销再登录），重复第 1 到 3 步；再改回英文。
  5. 在 Win11（自带 WebView2）上用环境变量模拟：打开命令提示符，执行 `set WEBVIEW2_BROWSER_EXECUTABLE_FOLDER=C:\no-such-folder`，然后在同一个窗口里运行绿色版的 `EmailFileViewer.exe`。这是模拟，如果这台机器上不弹对话框，在“现象”里写“模拟无效”，不算失败，以 Win10 的真实结果为准。
  6. 装好 WebView2（让 PORT-01 到 PORT-05 可以继续做）。
- **预期**：
  - 不是闪退：出现系统的消息框，标题是 `EmailFileViewer`，带错误图标，在所有窗口之上。
  - 英文系统显示英文：说明需要 Microsoft Edge WebView2 Runtime、给出 `https://developer.microsoft.com/microsoft-edge/webview2/`、让用户装好之后再启动；简体中文系统显示对应的中文。复制到记事本的文字与对话框一致。
  - 点“确定”之后进程退出，任务管理器里没有残留；exe 旁边没有新增 `data` 文件夹（检测在创建数据目录之前）。
  - 这个检测对安装版同样有效：装好安装版之后，如果能卸载掉 WebView2（Win10），再启动安装版会看到同样的对话框；这一条是可选的，做了就把结果写进“现象”。

#### PORT-07 绿色版与安装版同时运行时的单实例行为
- **规格**：无（design.md D13 的单实例说明，`README.txt` 里写了这条）
- **样本**：`samples/headers-normal.eml`、`samples/body-rtf-only.msg`
- **前置**：同一台机器上既有安装版又有绿色版。这一项要在第 5 章卸载之前做。
- **步骤**：
  1. 启动安装版，打开 `samples/headers-normal.eml`。
  2. 在命令提示符里运行绿色版：`EmailFileViewer.exe samples\body-rtf-only.msg`。
  3. 退出所有实例。启动绿色版，再在命令提示符里运行安装版：`"%LOCALAPPDATA%\EmailFileViewer\emailfileviewer.exe" samples\headers-normal.eml`（路径以实际安装目录为准）。
- **预期**：
  - 第 2 步：没有出现第二个窗口，后启动的进程很快退出；安装版的窗口被带到最前，新增一个标签页显示 `body-rtf-only.msg`，用的是安装版的设置。
  - 第 3 步：反过来，绿色版里新增一个标签页，用的是绿色版的设置。
  - 任务管理器里始终只有一个 `EmailFileViewer` 主程序进程。

#### PORT-08 绿色版压缩包的内容
- **规格**：app-distribution / 安装包形态（Windows 绿色版）；发布产物可追溯
- **样本**：无
- **步骤**：
  1. 不解压，列出压缩包里的文件：

     ```powershell
     Add-Type -AssemblyName System.IO.Compression.FileSystem
     [IO.Compression.ZipFile]::OpenRead((Resolve-Path .\EmailFileViewer_<版本>_x64-portable.zip)).Entries | Select-Object FullName, Length
     ```

  2. 解压后和安装版对比二进制：

     ```powershell
     Get-FileHash .\EmailFileViewer.exe, .\efv-sidecar.exe -Algorithm SHA256
     Get-FileHash "$env:LOCALAPPDATA\EmailFileViewer\emailfileviewer.exe", "$env:LOCALAPPDATA\EmailFileViewer\efv-sidecar.exe" -Algorithm SHA256
     ```

     （安装目录以安装程序实际显示的路径为准；安装版和绿色版必须来自同一个版本。）
  3. 9.5 完成后：对压缩包里的 `EmailFileViewer.exe` 和 `efv-sidecar.exe` 各执行 `Get-AuthenticodeSignature`。
- **预期**：
  - 压缩包根目录恰好 4 个文件：`EmailFileViewer.exe`、`efv-sidecar.exe`、`EmailFileViewer.portable`、`README.txt`，没有 `data` 文件夹。
  - 两份二进制与安装版的哈希相同（绿色版和安装版是同一份二进制）。
  - 9.5 完成后两个 exe 的 `Status` 都是 `Valid`。当前构建未签名，此时这一步填 `N/A`，备注写“9.5 未完成”。

### 3.2 打开文件（email-opening）

#### OPEN-01 通过文件对话框打开单个 .msg 文件
- **规格**：email-opening / 通过文件对话框打开 / 选择单个文件
- **样本**：`samples/body-rtf-only.msg`
- **步骤**：
  1. 点空状态里的 “Open file…”（“打开文件…”）。
  2. 在对话框里确认默认筛选器是 “Email files (*.eml, *.msg)”（“邮件文件 (*.eml, *.msg)”）：`samples/` 目录里只看得到 `.eml` 和 `.msg`，看不到 `README.md`、`ATTRIBUTION.md`。
  3. 把筛选器切换成 “All files”（“所有文件”），确认 `README.md` 等出现；再切回邮件文件。
  4. 选 `samples/body-rtf-only.msg`，确认。
- **预期**：邮件被打开，显示邮件头和正文（正文含 `RTF-ONLY-BODY`）；只有一个文件，所以**不显示标签栏**。

#### OPEN-02 取消文件对话框
- **规格**：email-opening / 通过文件对话框打开 / 取消对话框
- **样本**：`samples/body-rtf-only.msg`（沿用 OPEN-01 已打开的邮件）
- **步骤**：邮件已打开的状态下，点工具栏的 “Open…”（“打开…”），在对话框里点取消（或按 Esc）。
- **预期**：当前状态完全不变，没有错误提示，没有新标签页。

#### OPEN-03 在对话框里一次选多个文件
- **规格**：email-opening / 一次打开多个文件（多选）；通过文件对话框打开
- **样本**：`samples/headers-normal.eml`、`samples/body-plain-only.eml`、`samples/body-rtf-only.msg`
- **步骤**：打开对话框，用 Ctrl（macOS 用 ⌘）点击同时选中这三个文件，确认。
- **预期**：出现标签栏，三个文件各占一个标签页，标签上是不带扩展名的文件名；每个标签页里的邮件都能独立查看。

#### OPEN-04 拖入一个 EML 文件
- **规格**：email-opening / 通过拖放打开 / 拖入一个 EML 文件
- **样本**：`samples/headers-normal.eml`
- **步骤**：应用在空状态。在文件管理器（Finder、资源管理器、文件）里把 `samples/headers-normal.eml` 用鼠标拖到应用窗口上，松开。
- **预期**：拖入时窗口出现遮罩，提示 “Release to open 1 file”（“松开以打开 1 个文件”）；松开后遮罩消失，邮件被打开并显示，主题是 `Quarterly planning meeting`。遮罩上的文件数取决于 WebView 能否在拖动途中拿到文件列表；如果某个平台拖动时没有遮罩或没有数量，但松开后文件正常打开，在备注里写明，不算失败。

#### OPEN-05 真实拖放：一次拖入多个文件
- **规格**：email-opening / 通过拖放打开；一次打开多个文件（任务 5.6 的真实拖放）
- **样本**：`samples/headers-normal.eml`、`samples/body-html-and-plain.eml`、`samples/charset-gbk.eml`
- **步骤**：
  1. 在文件管理器里选中这三个文件，一起拖到窗口上，**先不松手**，在窗口上方停留一会儿。
  2. 松开。
  3. 在已经有邮件打开的状态下再拖一次 `samples/body-plain-only.eml` 和 `samples/charset-big5-undeclared.eml`。
- **预期**：
  - 停留时遮罩显示 “Release to open 3 files”（“松开以打开 3 个文件”）和说明 “Each file opens in its own tab. Text and links are ignored.”。
  - 松开后每个文件各开一个标签页（共 3 个），遮罩消失。
  - 第 3 步在原有标签页之外新增 2 个标签页，已有的不受影响。
  - 在 Windows 上，如果拖不进去，先确认应用不是以管理员身份运行。
  - 拖动途中没有遮罩或数量不对、但松开后三个文件都正常打开，只在备注里写明，不算失败。

#### OPEN-06 真实拖放：拖入文本和链接
- **规格**：email-opening / 通过拖放打开 / 拖入非文件内容
- **样本**：无（用文本和网页链接）
- **步骤**：
  1. 在任意文本编辑器里选中一段文字，拖到应用窗口上，松开。
  2. 在浏览器里把地址栏里的网址（或页面上的一个链接）拖到应用窗口上，松开。
  3. 之后再拖一个正常的 `samples/headers-normal.eml` 进去。
- **预期**：拖入文本和链接时没有遮罩残留、没有新标签页、没有错误提示（也没有通知）；窗口状态不变；第 3 步仍然能正常打开文件。

#### OPEN-07 双击 .msg 文件启动应用
- **规格**：email-opening / 通过系统文件关联打开 / 双击文件启动应用
- **样本**：`samples/body-rtf-only.msg`
- **步骤**：
  1. 完全退出应用，用 1.7 的命令确认没有进程。
  2. 在文件管理器里用本应用打开 `samples/body-rtf-only.msg`：
     - Windows：双击。第一次可能提示“你要如何打开这个文件”，选 EmailFileViewer 并勾选“始终使用”。也可以右键 → 打开方式 → 选择其他应用。
     - macOS：右键 → 打开方式 → EmailFileViewer。想让双击生效，先“显示简介 → 打开方式 → 全部更改…”。
     - Linux：文件管理器右键 → 使用其他应用打开 → EmailFileViewer。Fedora 要先做 1.4 的桌面集成。
  3. 数主程序进程。
- **预期**：应用启动并直接显示该文件的内容（正文含 `RTF-ONLY-BODY`），不需要再点“打开”；主程序进程数是 1。

#### OPEN-08 命令行传入路径
- **规格**：email-opening / 通过系统文件关联打开 / 命令行传入路径；一次打开多个文件（多个路径）
- **样本**：`samples/headers-normal.eml`、`samples/body-plain-only.eml`
- **步骤**：完全退出应用。在包含 `samples/` 的目录里打开终端，**用相对路径**执行（路径按终端当前目录解析）：
  - Windows：`& "$env:LOCALAPPDATA\EmailFileViewer\emailfileviewer.exe" .\samples\headers-normal.eml`（安装目录和文件名以实际为准）
  - macOS：`/Applications/EmailFileViewer.app/Contents/MacOS/emailfileviewer samples/headers-normal.eml`
  - Linux（deb）：`emailfileviewer samples/headers-normal.eml`
  - Linux（AppImage）：`./EmailFileViewer_<版本>_amd64.AppImage samples/headers-normal.eml`

  看到内容后退出，再执行一次，这次带两个路径：`... samples/headers-normal.eml samples/body-plain-only.eml`。
- **预期**：应用启动并直接显示该文件；两个路径时开出两个标签页。

#### OPEN-09 “打开方式”列表里有本应用，.eml 和 .msg 都有
- **规格**：email-opening / 通过系统文件关联打开（安装后注册为 .eml 和 .msg 的可选打开方式）
- **样本**：`samples/headers-normal.eml`、`samples/body-rtf-only.msg`
- **步骤**：
  - Windows：右键两个文件 → 打开方式，看列表（需要时点“选择其他应用”）。
  - macOS：右键两个文件 → 打开方式，看列表。
  - Linux：先执行下面的命令，再在文件管理器里右键 → 使用其他应用打开：

    ```sh
    xdg-mime query filetype samples/body-rtf-only.msg   # 预期 application/vnd.ms-outlook
    gio mime message/rfc822
    gio mime application/vnd.ms-outlook
    ```
- **预期**：两种文件的“打开方式”里都有 EmailFileViewer。Linux 上 `gio mime` 的 `Registered applications` 列表里有本应用的 `.desktop` 文件。（macOS 上 `.eml` 通过 `com.apple.mail.email` 这个 UTI 登记，`.msg` 按扩展名匹配，见 `docs/release.md`。）

#### OPEN-10 应用运行中再双击另一个文件：已有实例处理，只有一个进程
- **规格**：email-opening / 单实例转发 / 应用运行中再双击另一个文件
- **样本**：`samples/headers-normal.eml`（先打开）、`samples/body-html-and-plain.eml`（后双击）
- **步骤**：
  1. 用 OPEN-07 的办法启动应用并打开 `samples/headers-normal.eml`，数进程，记下数量。
  2. 变体 A：把应用窗口最小化。变体 B：用别的应用窗口把它盖住。
  3. 在文件管理器里双击（或“打开方式”）`samples/body-html-and-plain.eml`。
  4. 数主程序进程和 `efv-sidecar` 进程。
- **预期**：已运行的实例新增一个标签页并打开该文件（出现标签栏，共 2 个标签页）；窗口从最小化恢复或被带到最前并获得焦点；主程序进程数仍是 1，`efv-sidecar` 进程数也是 1；Dock、任务栏、窗口列表里没有第二个窗口或第二个图标。变体 A、B 都要通过。

#### OPEN-11 应用运行中再从命令行启动：转发给已有实例
- **规格**：email-opening / 单实例转发（通过系统或命令行再次打开）
- **样本**：`samples/body-plain-only.eml`
- **步骤**：应用已在运行时，在另一个终端里再执行一次 OPEN-08 的命令，路径换成 `samples/body-plain-only.eml`，注意命令是否很快返回。macOS 另外再试 `open -a EmailFileViewer samples/body-plain-only.eml`。
- **预期**：命令很快返回（第二个进程自己退出）；已运行的实例新增一个标签页，窗口被带到前台；主程序进程数仍是 1。

#### OPEN-12 扩展名与内容不符
- **规格**：email-opening / 按内容识别文件格式 / 扩展名与内容不符
- **样本**：`samples/format-msg-disguised-as-eml.eml`（扩展名 .eml，内容是 MSG）、`samples/format-eml-disguised-as-msg.msg`（扩展名 .msg，内容是 EML）
- **步骤**：用对话框把这两个文件一起打开。
- **预期**：两个都正常显示，没有错误标签页。前者的正文含 `RTF-ONLY-BODY`；后者的主题是 `Plain text only: paragraphs and blank lines`。

#### OPEN-13 打开图片等不是邮件的文件
- **规格**：email-opening / 无法识别或损坏的文件 / 打开一张图片
- **样本**：`samples/invalid/not-an-email.png`、`samples/invalid/png-disguised-as-eml.eml`、`samples/invalid/empty.eml`
- **步骤**：
  1. 对话框里把筛选器切到 “All files”，选 `samples/invalid/not-an-email.png` 打开。
  2. 再用对话框（或拖放）打开 `samples/invalid/png-disguised-as-eml.eml` 和 `samples/invalid/empty.eml`。
  3. 随后打开一个正常文件 `samples/headers-normal.eml`。
- **预期**：每个失败的文件占一个带警示图标的标签页，页面上显示原因 “Unsupported format”（“不支持的格式”）、标题 “This file isn't a supported email”（“这不是受支持的邮件文件”），并写出文件名；页面上有 “Open another file” 和 “Close tab” 按钮；应用没有崩溃或卡住，第 3 步的正常文件照常打开。

#### OPEN-14 解析异常的文件：损坏的 MSG，以及 sidecar 进程被杀掉
- **规格**：email-opening / 无法识别或损坏的文件 / 解析过程中崩溃
- **样本**：`samples/invalid/truncated.msg`、`samples/invalid/truncated-half.eml`、`samples/headers-normal.eml`、`samples/EmailWithMsgAttachment.msg`
- **自动化覆盖**：解析进程“恰好在解析某个文件时崩溃”无法用样本稳定复现（没有样本能让 sidecar 崩溃），这部分由自动化测试覆盖，见附录 B。下面的手动步骤验证它的外围行为。
- **步骤**：
  1. 打开 `samples/invalid/truncated.msg`。
  2. 打开 `samples/invalid/truncated-half.eml`（MimeKit 能容忍截断，它不是错误文件）。
  3. 打开 `samples/headers-normal.eml` 和 `samples/EmailWithMsgAttachment.msg`，在后者的标签页里先**不要**点附件。
  4. 手动结束 sidecar：Windows `taskkill /IM efv-sidecar.exe /F`，macOS 和 Linux `pkill -x efv-sidecar`。
  5. 回到 `samples/EmailWithMsgAttachment.msg` 的标签页，点嵌套邮件附件的 “Open”（“打开”）。
  6. 再用对话框打开 `samples/body-plain-only.eml`。
- **预期**：
  - 第 1 步：错误标签页，原因 “Damaged file”（“文件已损坏”），应用不退出。
  - 第 2 步：邮件打开，显示截断前的内容（含第一个附件），不报错、不崩溃。
  - 第 4 步之后：应用界面没有任何变化，也没有退出。
  - 第 5 步：嵌套邮件正常进入下一层（sidecar 被自动重新拉起，旧文档被自动恢复）。
  - 第 6 步：新文件正常打开；再看进程列表，`efv-sidecar` 又出现了一个。

#### OPEN-15 一次拖入三个文件，其中一个已损坏
- **规格**：email-opening / 一次打开多个文件 / 多个文件中有一个损坏
- **样本**：`samples/headers-normal.eml`、`samples/invalid/truncated.msg`、`samples/body-plain-only.eml`
- **步骤**：在文件管理器里选中这三个文件（可以先复制到同一个文件夹里），一次拖到窗口上。
- **预期**：
  - 出现三个标签页：两个正常打开，损坏的那个是错误标签页（“Damaged file”）。
  - 窗口右上角出现一条黄色汇总通知 “Opened 2 of 3 files”（“已打开 3 个文件中的 2 个”），列出 `truncated.msg` 和原因，带 “Show”（“查看”）和 “Dismiss”（“关闭”）按钮；点 “Show” 切到错误标签页。
  - 另外两封邮件可以正常查看，关闭错误标签页不影响它们。

#### OPEN-16 无法读取的文件
- **规格**：email-opening / 无法识别或损坏的文件（原因类别“无法读取”，规格没有对应的独立场景）
- **样本**：`samples/efv-unreadable.eml`（手工创建），`samples/does-not-exist.eml`（不存在的路径）
- **步骤**：
  1. 创建一个没有读权限的文件（用普通用户，不要用 root 或管理员）：
     - macOS、Linux：`cp samples/headers-normal.eml samples/efv-unreadable.eml && chmod 000 samples/efv-unreadable.eml`
     - Windows：`Copy-Item samples\headers-normal.eml samples\efv-unreadable.eml; icacls samples\efv-unreadable.eml /deny "${env:USERNAME}:(R)"`
  2. 把它拖到窗口里。
  3. 用命令行启动应用并传入不存在的路径：`... samples/does-not-exist.eml`（命令同 OPEN-08）。
  4. 清理：macOS、Linux `chmod 600 samples/efv-unreadable.eml && rm samples/efv-unreadable.eml`；Windows `icacls samples\efv-unreadable.eml /remove:d "$env:USERNAME"; Remove-Item samples\efv-unreadable.eml`。
- **预期**：两种情况都显示错误标签页，原因 “Can't read file”（“无法读取”）、标题 “This file can't be read”（“无法读取这个文件”），并写出文件名；应用保持可用。

### 3.3 标签页、快捷键与窗口标题（任务 5.6、7.10）

#### TAB-01 标签栏的显示规则与基本操作
- **规格**：design.md D7“单窗口加标签页”（无对应规格场景）
- **样本**：`samples/headers-normal.eml`、`samples/body-plain-only.eml`、`samples/body-html-and-plain.eml`
- **步骤**：
  1. 打开 1 个文件，再依次打开另外 2 个。
  2. 点标签切换；聚焦标签后用键盘左右方向键、Home、End 切换。
  3. 点标签上的关闭按钮关掉一个标签页，直到只剩 1 个，再关掉最后一个。
  4. 点标签栏右端的 “+”。
- **预期**：只有 1 个文件时不显示标签栏，2 个及以上时显示；标签上是不带扩展名的文件名，悬停显示完整文件名；方向键能切换并激活标签页；剩 1 个时标签栏重新隐藏；关掉最后一个后回到空状态（标题 “Open an email file”，有隐私说明 “Messages are read on this computer. Nothing is uploaded unless you choose to.”）；“+” 弹出文件对话框。

#### KEY-01 macOS：⌘O 打开文件
- **规格**：design.md D11 补充（原生菜单）
- **样本**：`samples/headers-normal.eml`、`samples/body-plain-only.eml`
- **步骤**：应用在前台，按 ⌘O。选两个文件确认。再点一下邮件正文，让键盘焦点进入正文，再按 ⌘O。
- **预期**：两次都弹出系统文件对话框（菜单“文件 → 打开…”）；选中的文件各开一个标签页。

#### KEY-02 macOS：⌘W 关闭当前标签页，焦点在正文以外
- **规格**：design.md D11 补充（原生菜单）
- **样本**：`samples/headers-normal.eml`、`samples/body-plain-only.eml`
- **步骤**：打开这两个文件，点一下邮件头或工具栏的空白处，按 ⌘W；再按一次 ⌘W 关掉最后一个标签页。
- **预期**：每按一次关闭当前一个标签页；关掉最后一个后回到空状态，**窗口和应用都不退出**。

#### KEY-03 macOS：焦点在正文里时 ⌘W 仍能关闭标签页
- **规格**：任务 7.10（“焦点在正文里时 ⌘W”）
- **样本**：`samples/body-html-and-plain.eml`、`samples/headers-normal.eml`
- **步骤**：打开这两个文件。在邮件正文区域里点一下（再按住拖选一段文字，确保键盘焦点在正文 iframe 里），按 ⌘W。
- **预期**：当前标签页被关闭，剩下另一个；不是窗口被关闭，也不是没反应。这是 7.10 里唯一没做过真机验证的一条，请重点确认，并且在 HTML 邮件和纯文本邮件的正文里各试一次。

#### KEY-04 macOS：⌘, 打开设置
- **规格**：任务 7.10（原生菜单“设置…”）
- **样本**：无
- **步骤**：按 ⌘,；在设置对话框已打开时再按一次；关闭后，焦点在正文里再按一次。
- **预期**：打开设置对话框（标题 “Settings”，有语言和外观两节）；重复按不会叠出第二个；焦点在正文里也能打开。

#### KEY-05 macOS：菜单栏内容与复制
- **规格**：任务 7.10（原生菜单，包含标准的编辑菜单）
- **样本**：`samples/body-plain-only.eml`
- **步骤**：
  1. 逐个展开菜单栏：应用菜单、文件、编辑、窗口。
  2. 在正文里拖选一段文字，按 ⌘C，到别的应用里粘贴。
  3. 点正文，用菜单“编辑 → 全选”，再按 ⌘C，粘贴。
  4. 点应用菜单里的 “About EmailFileViewer”。
- **预期**：
  - 应用菜单有关于、设置（⌘,）、隐藏、退出。
  - 文件菜单有打开（⌘O）、关闭标签页（⌘W）。
  - 编辑菜单有撤销、重做、剪切、复制、粘贴、全选。
  - 窗口菜单有最小化、缩放。
  - 复制出来的文字与正文一致。
  - 关于对话框里能看到版本号（填进 1.1）。

#### KEY-06 Windows 和 Linux：没有菜单栏
- **规格**：design.md D11 补充（Windows 和 Linux 不显示菜单栏）
- **样本**：无
- **步骤**：启动应用，看窗口顶部；按一下 Alt 键。
- **预期**：窗口顶部没有菜单栏，Alt 也不会弹出菜单；打开设置只能点工具栏右端的齿轮图标（Settings）。

#### KEY-07 Windows 和 Linux：Ctrl+O 打开文件（焦点在正文以外）
- **规格**：任务 7.10（Windows 和 Linux 沿用网页内的快捷键）
- **样本**：`samples/headers-normal.eml`
- **步骤**：应用启动后（空状态）直接按 Ctrl+O；选文件确认。再点一下邮件头的空白处，按 Ctrl+O。
- **预期**：两次都弹出文件对话框，选中的文件被打开。

#### KEY-08 Windows 和 Linux：Ctrl+W 关闭当前标签页（焦点在正文以外）
- **规格**：任务 7.10
- **样本**：`samples/headers-normal.eml`、`samples/body-plain-only.eml`
- **步骤**：打开这两个文件，点一下邮件头或工具栏空白处，按 Ctrl+W；再按一次。
- **预期**：每次关闭当前一个标签页；关掉最后一个后回到空状态，窗口不关闭。

#### KEY-09 Windows 和 Linux：焦点在正文里时的已知限制
- **规格**：design.md D11 补充（已知限制）
- **样本**：`samples/body-html-and-plain.eml`、`samples/headers-normal.eml`
- **步骤**：打开这两个文件，在正文里点一下，按 Ctrl+W；然后点一下邮件头空白处，再按 Ctrl+W。
- **预期**：按设计，**焦点在正文里时 Ctrl+O、Ctrl+W 不生效**（键盘事件到不了应用页面）；点一下正文以外的区域后恢复。符合这个描述就记 ✅。如果焦点在正文里时反而生效了，也记 ✅，在备注里写明。

#### TITLE-01 窗口标题跟随当前邮件
- **规格**：任务 7.10 / design.md D11 补充（窗口标题）
- **样本**：`samples/headers-normal.eml`、`samples/RtfWithShortRussianString.msg`（主题为空）、`samples/nested-rfc822-two-levels.eml`、`samples/invalid/not-an-email.png`
- **步骤**：
  1. 空状态下看窗口标题。
  2. 打开 `samples/headers-normal.eml`，看标题。
  3. 打开 `samples/RtfWithShortRussianString.msg`，看标题；在它与第一封之间切换标签页。
  4. 打开 `samples/nested-rfc822-two-levels.eml`，进入嵌套邮件（点附件的 “Open”）到第 2 层，再点面包屑或 “Back”（“返回”）回到第 1 层。
  5. 打开 `samples/invalid/not-an-email.png`（错误标签页）。
  6. 关闭所有标签页。
- **预期**：
  - 空状态：`EmailFileViewer`。
  - 第 2 步：`Quarterly planning meeting — EmailFileViewer`。
  - 第 3 步：没有主题，用文件名，即 `RtfWithShortRussianString.msg — EmailFileViewer`；切换标签页时标题跟着变。
  - 第 4 步：第 1 层是 `Level 1 (outer) message with two nested levels — EmailFileViewer`，进入第 2 层变成 `Level 2 (middle) message — EmailFileViewer`，返回后变回去。
  - 第 5 步：错误标签页的标题用它的文件名。
  - 第 6 步：回到 `EmailFileViewer`。
  - Linux 的 GNOME 在标题栏里可能不显示标题，此时用活动概览、Alt+Tab 或窗口列表查看。

### 3.4 邮件显示（email-viewing）

沙箱、远程内容和网络请求三个需求的场景在第 4 章（SEC-01 到 SEC-08），链接的场景在 3.5。

#### VIEW-01 普通邮件：显示邮件头
- **规格**：email-viewing / 显示邮件头 / 普通邮件
- **样本**：`samples/headers-normal.eml`
- **步骤**：打开样本，核对邮件头。把鼠标悬停在日期上。
- **预期**：
  - 主题：`Quarterly planning meeting`。
  - From：`Alice Zhang` 和 `alice.zhang@example.com`。
  - To：`Bob Li` `bob.li@example.org`，`Carol Wang` `carol.wang@example.net`。
  - Cc：`Dave Chen` `dave.chen@example.com`。
  - Date：本地时间，按界面语言格式化；悬停显示原始的 `Thu, 01 Oct 2026 10:00:00 +0800`。
  - 每个地址都同时有名称和邮箱地址；正文四行文字，换行保留。

#### VIEW-02 显示密送和回复地址；没有抄送时不出现空栏
- **规格**：email-viewing / 显示邮件头（密送、回复地址）；缺少某个邮件头
- **样本**：`samples/headers-timezone-bcc-replyto-no-cc.eml`
- **步骤**：打开样本，核对邮件头。
- **预期**：有 “Reply to”（“回复至”）一行，值是 `Alice Zhang (Support)` `support@example.com`；有 To 一行 `Bob Li` `bob.li@example.org`；有 “Bcc”（“密送”）一行 `Erin Xu` `erin.xu@example.org`；**没有 Cc 一行，也没有空白占位或“Cc: —”之类的东西**。

#### VIEW-03 日期按本地时区显示
- **规格**：email-viewing / 日期按本地时区显示 / 跨时区邮件
- **样本**：`samples/headers-timezone-bcc-replyto-no-cc.eml`（`Date: Mon, 5 Oct 2026 09:00:00 -0700`）
- **自动化覆盖**：跨时区换算另有自动化测试：`ui/src/__tests__/viewer-mail.test.tsx`（“7.4 场景‘日期按本地时区显示：跨时区邮件’”）和 `core/tests/EmailFileViewer.Core.Tests/EmlHeaderTests.cs`。这里用真实系统时区再验一遍。
- **步骤**：
  1. 把系统时区设成 UTC+8（1.7），重启应用，打开样本，英文界面和中文界面各看一次。
  2. 把系统时区改成美国太平洋时间（10 月是 UTC-7），重启应用，再看一次。
  3. 恢复原来的时区。
- **预期**：
  - UTC+8：英文界面 `Tue, Oct 6, 2026, 12:00 AM`，中文界面 `2026年10月6日周二 00:00`，即 2026-10-06 00:00。
  - UTC-7：英文界面 `Mon, Oct 5, 2026, 9:00 AM`。
  - 悬停显示的原始头始终是 `Mon, 5 Oct 2026 09:00:00 -0700`。

#### VIEW-04 同时有 HTML 和纯文本时显示 HTML
- **规格**：email-viewing / 正文格式选择 / 同时有 HTML 和纯文本
- **样本**：`samples/body-html-and-plain.eml`
- **步骤**：打开样本。
- **预期**：看到大标题 `HTML-VERSION`、粗体和斜体的文字、一个列表和一个链接；**看不到** `PLAIN-TEXT-VERSION`。

#### VIEW-05 只有 RTF 正文的 MSG
- **规格**：email-viewing / 正文格式选择 / 只有 RTF 正文的 MSG
- **样本**：`samples/body-rtf-only.msg`；补充：`samples/body-html-in-rtf.msg`、`samples/RtfSampleEmail.msg`
- **步骤**：打开样本，从上到下核对。
- **预期**：
  - `body-rtf-only.msg`：从 `RTF-ONLY-BODY` 开始，到 `END-OF-RTF-BODY` 结束；有粗体、斜体、下划线、彩色文字，两个段落（段落之间有间隔），项目符号列表，一行等宽字体，一个超链接，以及中文 `Chinese text: 中文段落。`；**看不到** `{\rtf1`、`\par`、`\b` 之类的 RTF 控制字。
  - `body-html-in-rtf.msg`：从 `HTML-IN-RTF-BODY` 到 `END-OF-HTML-IN-RTF-BODY`，有标题、粗斜体、链接、中文、列表和表格，没有 `\htmltag` 之类的残留。
  - `RtfSampleEmail.msg`：正文可读，没有 RTF 控制字。

#### VIEW-06 纯文本保留换行
- **规格**：email-viewing / 正文格式选择（纯文本正文保留换行）
- **样本**：`samples/body-plain-only.eml`；补充：`samples/TxtSampleEmail.msg`
- **步骤**：打开样本，核对正文。
- **预期**：段落之间的空行保留，`Third paragraph comes after TWO blank lines.` 之前有两个空行的间隔；`Indented line with four spaces.` 和制表符缩进的那一行保持缩进；`> quoted line` 两行原样显示；`<b>not bold</b> & "quotes" 'single'` 作为**普通文字**显示，没有变粗；网址行是文字；最后的中文句子 `中文段落，用来确认 UTF-8 纯文本也能正确显示。` 没有乱码。`TxtSampleEmail.msg` 按纯文本显示，不当成富文本。

#### VIEW-07 GBK 编码的中文邮件
- **规格**：email-viewing / 字符集解码 / GBK 编码的中文邮件
- **样本**：`samples/charset-gbk.eml`
- **步骤**：打开样本。
- **预期**：主题 `测试邮件：GBK 编码的中文主题`；发件人名称 `张三`；正文以 `您好，` 开头，包含 `常用字：你好世界，邮件查看器，附件，发件人，收件人。`；没有乱码、没有问号或方块。

#### VIEW-08 未声明字符集（Big5）
- **规格**：email-viewing / 字符集解码 / 未声明字符集
- **样本**：`samples/charset-big5-undeclared.eml`
- **步骤**：打开样本。
- **预期**：主题 `公司內部公告：專案進度報告`；正文以 `各位同仁大家好：` 开头，是约 300 字的繁体中文公告，包含 `專案`、`週`、`準時`；没有乱码。

#### VIEW-09 其他字符集与 RFC 2047 邮件头
- **规格**：email-viewing / 字符集解码（GB2312、GB18030、Big5、Shift_JIS、ISO-2022-JP 与 RFC 2047）
- **样本**：见下表
- **步骤**：把下表里的文件一起打开，逐个核对。
- **预期**：

  | 样本 | 主题 | 其他要点 |
  |------|------|----------|
  | `samples/charset-gb18030.eml` | `GB18030 样本：包含四字节字符` | 正文含四字节字符 `𠮷`、`𠀀` 和 `€`（缺字体时这几个可能显示成方块，换有字体的系统再确认） |
  | `samples/charset-shift-jis.eml` | `文字コードのテスト：Shift_JIS` | 正文以 `お疲れ様です。` 开头 |
  | `samples/charset-iso-2022-jp.eml` | `文字コードのテスト：ISO-2022-JP` | 发件人名称 `山田太郎`；正文以 `お世話になっております。` 开头 |
  | `samples/charset-iso-2022-jp-thunderbird.eml` | `日本語メールテスト (testing Japanese emails)` | 第一行是 `Let's see if both subject and body works fine...`，其后是日文 |
  | `samples/charset-koi8-r-raw-header.eml` | `Откуда он появился?` | 邮件头里直接写 KOI8-R 字节（没有 RFC 2047），不能显示成 `ïÔËÕÄÁ ÏÎ ÐÏÑ×ÉÌÓÑ?` |
  | `samples/charset-gbk-raw-header.eml` | `老邮件：邮件头直接写 GBK 字节` | 发件人名称 `李四`（`li.si@example.com`） |
  | `samples/headers-rfc2047.eml` | `月度报告 Report （含附件 — résumé）` | 发件人名称 `李 Li 明 (Ming)`；第一个收件人名称 `Müller 先生` |

#### VIEW-10 分段正文（Apple Mail 式）：图片夹在文字之间
- **规格**：email-viewing / 正文格式选择；email-attachments / 列出附件（夹在正文里的内嵌图片不列出）
- **样本**：`samples/body-multipart-mixed-inline-images.eml`
- **步骤**：打开样本，从上到下看正文，再看附件区。
- **预期**：正文按顺序是三段文字（`SEGMENT-ONE`、`SEGMENT-TWO`（含中文）、`SEGMENT-THREE`），两张图片（`photo.jpg`、`logo.png`）以内嵌图片的形式插在文字之间；**看不到** `PLAIN-ALTERNATIVE`；附件列表**只有** `itinerary.pdf`。

#### VIEW-11 内嵌图片（cid）显示在正文对应位置
- **规格**：email-viewing / 内嵌图片 / 带签名图片的邮件
- **样本**：`samples/cid-inline-image.eml`；补充：`samples/TestWithAttachmentsAndInlines.eml`、`samples/EmailWithAttachments.msg`
- **步骤**：打开样本（建议断网状态下做）。
- **预期**：`cid-inline-image.eml` 的正文里 logo 和签名图在对应位置显示，不是裂图；**没有**“远程内容已阻止”的提示条（这个样本没有任何远程引用）。`TestWithAttachmentsAndInlines.eml` 里的 `Outlook-facebook.png` 显示在正文里。`EmailWithAttachments.msg` 的正文里按文件名引用的 `tinkerbell.jpg` 显示出来。

#### VIEW-12 其余样本逐个打开
- **规格**：email-viewing / 显示邮件头；正文格式选择；字符集解码（补足用到全部样本）
- **样本**：见下表
- **步骤**：打开表里的文件，核对。
- **预期**：

  | 样本 | 预期 |
  |------|------|
  | `samples/EmailWithMultilineSubject.eml` | 主题完整显示，折叠的多行被合成一行，没有残留的换行或多余空格 |
  | `samples/simple-multipart.eml` | 邮件头和正文正常，附件区有 1 个附件 |
  | `samples/RtfWithShortRussianString.msg` | 没有主题行、没有发件人行；正文是俄文（cp1251），无乱码；窗口标题用文件名 |
  | `samples/HtmlSampleEmailWithAttachment.msg` | HTML 正文（从 RTF 里还原）正常显示；附件 `SampleAttachment.pdf` |
  | `samples/EmailWithAttachments.msg` | HTML 正文；有收件人、抄送、密送各一个；附件只有 `peterpan.jpg` |
  | `samples/TxtSampleEmail.msg` | 纯文本正文，不当成富文本 |

### 3.5 正文链接（email-viewing / 正文链接在系统浏览器中打开）

链接的点击要用**真实的鼠标**，不要用键盘或脚本。macOS 上的真实点击在 5.7 里没有验证过（当时没有辅助功能权限），请务必在 macOS 上亲手点。

#### LINK-01 点击正文里的 https 链接：系统浏览器打开，应用里没有新窗口
- **规格**：email-viewing / 正文链接在系统浏览器中打开 / 点击正文链接
- **样本**：`samples/body-html-and-plain.eml`（正文里有 `link`）、`samples/link-text-mismatch.eml`（第 4 个链接）
- **步骤**：
  1. 打开 `samples/body-html-and-plain.eml`，记下标签页数和窗口数。
  2. 用鼠标点正文里的 `link`。
  3. 回到应用，再点同一个链接两次。
  4. 打开 `samples/link-text-mismatch.eml`，点第 4 个链接 `https://example.com/`。
- **预期**：系统默认浏览器打开 `https://example.com/`；应用里正文还在、窗口标题不变、标签页数不变；**应用里没有出现新窗口**（任务栏、Dock、Alt+Tab 里窗口数不变）；多次点击结果一致。

#### LINK-02 链接文字与实际地址不符：悬停显示实际地址
- **规格**：email-viewing / 正文链接在系统浏览器中打开 / 链接文字与实际地址不符
- **样本**：`samples/link-text-mismatch.eml`
- **步骤**：用鼠标分别悬停在第 1 个链接（文字 `paypal.com`）、第 2 个链接（文字 `https://www.paypal.com/signin`）、第 3 个链接（文字 `Hover me`）上，**停住不动约 2 秒**。
- **预期**：三个链接都弹出提示，内容是实际地址 `https://example.com/not-paypal`；第 3 个链接原邮件里伪造了 `title="https://www.paypal.com/"`，这里**不应**显示 paypal 的地址。如果 WebView 同时在窗口左下角显示状态栏地址，那也必须是实际地址。一个提示都不出现，记为失败。

#### LINK-03 正文里的页内锚点：能滚动，点击后正文不消失
- **规格**：email-viewing / 正文链接在系统浏览器中打开（页内锚点，对应 Core 把 `#x` 改写为 `about:srcdoc#x` 的修复）
- **样本**：`samples/links-anchors-and-schemes.eml`（每个链接的预期行为也写在 `samples/README.md` 的“沙箱、链接与安全”表里）
- **步骤**：
  1. 打开样本。顶部是标题 `Links: anchors and schemes` 和 “Table of contents”，目录里有 Section 1 到 Section 4 和 Bottom 五个链接；下面是几组其他链接，再往下是五个很长的段落，依次以 `SECTION-1-MARKER` 到 `SECTION-4-MARKER`、`BOTTOM-MARKER` 开头。
  2. 依次点目录里的 “Section 1”、“Section 2”、“Section 3”、“Section 4”。每点一个，等滚动结束，再滚回顶部点下一个。
  3. 点目录里的 “Bottom (this anchor carries target="_blank")”。这个锚点带了 `target="_blank"`。
  4. 在最底部的段落里点 “Back to the top (href="#")”。
  5. 回到顶部，往下找到 “SVG links” 一节，点 “SVG link to #section-2 (xlink:href)”。
  6. 每一步之后都观察：邮件头、附件区、窗口标题、标签页。
- **预期**：
  - 第 2 步：每次点击后，正文区域内滚动，对应段落的标记文字（`SECTION-n-MARKER`）出现在可见区域的顶部附近。
  - 第 3 步：滚到最后一个段落，`BOTTOM-MARKER` 可见。尽管带了 `target="_blank"`，**仍然是在正文里滚动**：不开新窗口，不拉起浏览器。
  - 第 4 步：滚回到顶部，标题 `Links: anchors and schemes` 可见，**正文不会消失**。
  - 第 5 步：滚动到 `SECTION-2-MARKER`。
  - 每一步之后正文都还在（不会被换成应用自己的页面或空白）；邮件头、附件区没变，窗口标题仍是 `Links: anchors and schemes — EmailFileViewer`；标签页数不变；没有新窗口；没有浏览器被拉起。

#### LINK-04 正文里的右键菜单（观察项）
- **规格**：无（5.7 实验的补充）
- **样本**：`samples/links-anchors-and-schemes.eml`
- **步骤**：在正文里的一个链接上点右键；再在正文空白处点右键。如果出现菜单，记下全部菜单项，**只点“复制”之类无害的项**，最后按 Esc 关掉。
- **预期**：如果右键菜单里有“后退”“重新载入”“在新窗口中打开链接”之类的项，把菜单项原样写进“现象”并截图，**不要判为失败，也不要去点**（这些项可能把正文换成别的内容，请交给开发者判断）。没有菜单也没问题。本项只要记录就记 ✅。

### 3.6 加密与签名邮件（email-viewing / 加密与签名邮件提示）

#### SMIME-01 加密邮件
- **规格**：email-viewing / 加密与签名邮件提示 / 加密邮件
- **样本**：`samples/smime-encrypted.eml`、`samples/smime-msg-encrypted.msg`
- **步骤**：分别打开，核对邮件头和正文区域。
- **预期**：邮件头正常显示（发件人、收件人、主题、日期）；邮件头下方有 “Encrypted”（“已加密”）徽标；正文区域是占位页，标题 “This message is encrypted”（“这封邮件已加密”），说明 EmailFileViewer 无法解密 S/MIME 邮件；**看不到**正文内容 `SECRET-BODY`。

#### SMIME-02 签名邮件
- **规格**：email-viewing / 加密与签名邮件提示 / 签名邮件
- **样本**：`samples/smime-signed.eml`、`samples/smime-signed-opaque.eml`、`samples/smime-signed-thunderbird.eml`、`samples/smime-msg-signed-opaque.msg`、`samples/smime-msg-signed-clear.msg`
- **步骤**：逐个打开，核对邮件头徽标和正文；点徽标上的 “About this signature”（“关于此签名”）。
- **预期**：
  - 五封邮件的邮件头里都有 “Signed”（“已签名”）徽标，旁边注明 “signature not verified”（“签名未验证”）；点开说明文字是 “This message has a digital signature, but EmailFileViewer doesn't check signatures…”。
  - 正文都正常显示：`smime-signed-opaque.eml` 的正文含 `OPAQUE-SIGNED-HTML`，附件只有 `signed-report.pdf`；`smime-msg-signed-opaque.msg` 的正文以 `OPAQUE-SIGNED-BODY` 开头，附件只有 `opaque-note.txt`；`smime-msg-signed-clear.msg` 的正文以 `CLEAR-SIGNED-BODY` 开头；`smime-signed-thunderbird.eml` 的附件只有 `signed.jpg`。
  - 附件列表里**不出现** `smime.p7s`、`smime.p7m`。
  - 打开这些邮件的过程中**不产生网络请求**（抓包验证见 SEC-07）。

### 3.7 附件（email-attachments）

保存附件的输出目录统一用 `~/efv-out/`（见 1.5），每项开始前先建好。

#### ATT-01 列出附件：PDF 和 xlsx
- **规格**：email-attachments / 列出附件 / 带两个附件的邮件
- **样本**：`samples/attachments-pdf-xlsx-jpg.eml`
- **步骤**：打开样本，看附件区。
- **预期**：附件区标题 “Attachments”（“附件”）旁边是摘要，如 “3 files · …”；列出 `report.pdf`、`budget.xlsx`、`photo.jpg`，每个都有文件名和大小；有 “Save all…”（“全部保存…”）按钮。

#### ATT-02 内嵌图片不重复列出
- **规格**：email-attachments / 列出附件 / 内嵌图片不重复列出
- **样本**：`samples/cid-inline-image.eml`；补充：`samples/EmailWithAttachments.msg`
- **步骤**：打开样本，看正文和附件区。
- **预期**：`cid-inline-image.eml` 的附件区只有 `quarterly-report.docx`，摘要是 “1 file · …”，**没有** “Save all…” 按钮（只有 1 个附件时不显示）；两张 cid 图片只在正文里出现。`EmailWithAttachments.msg` 的附件区只有 `peterpan.jpg`。

#### ATT-03 保存单个附件，内容与原字节一致
- **规格**：email-attachments / 保存单个附件 / 保存 PDF 附件
- **样本**：`samples/attachments-pdf-xlsx-jpg.eml`；对照文件 `samples/assets/photo.jpg`
- **步骤**：
  1. 点 `report.pdf` 的保存图标（悬停提示 “Save…”）。系统保存对话框的默认文件名应是 `report.pdf`。先点取消。
  2. 再点一次，保存到 `~/efv-out/`。
  3. 同样保存 `photo.jpg` 到 `~/efv-out/`。
  4. 计算 `~/efv-out/photo.jpg` 和 `samples/assets/photo.jpg` 的 SHA-256（1.7）。
  5. 用 PDF 阅读器打开保存出来的 `report.pdf`。
- **预期**：取消保存对话框时没有错误提示；两个文件都生成；`photo.jpg` 的哈希与 `samples/assets/photo.jpg` **完全相同**（它是附件字节的原件）；`report.pdf` 能正常打开。

#### ATT-04 保存全部：目标文件夹已有同名文件时不覆盖
- **规格**：email-attachments / 保存全部附件 / 目标文件夹已有同名文件
- **样本**：`samples/attachments-duplicate-names.eml`（两个内容不同的 `report.pdf`，分别含 `Report A` 和 `Report B`）
- **步骤**：
  1. 建 `~/efv-out/dup/`，在里面放一个 `report.pdf`，内容随便写，例如 `ORIGINAL`；记下它的 SHA-256。
  2. 打开样本，点 “Save all…”，选择 `~/efv-out/dup/`。
  3. 再建一个空文件夹 `~/efv-out/dup2/`，再点一次 “Save all…” 选它。
  4. 列出两个文件夹，比较 `dup/report.pdf` 的哈希。
- **预期**：
  - `dup/` 里原来的 `report.pdf` 内容和哈希**完全不变**；新出现两个不冲突的文件，名字形如 `report (1).pdf`、`report (2).pdf`。
  - `dup2/` 里是 `report.pdf` 和 `report (1).pdf`；用 PDF 阅读器或文本编辑器看，一个含 `Report A`，一个含 `Report B`，没有互相覆盖。
  - 完成后出现通知 “Saved 2 attachments”（“已保存 2 个附件”）。

#### ATT-05 附件文件名的安全处理（路径穿越、保留名、非法字符）
- **规格**：email-attachments / 附件文件名的安全处理 / 文件名包含路径穿越
- **样本**：`samples/attachments-unsafe-names.eml`（7 个附件：`../../.bashrc`、`..\..\Windows\evil.txt`、`/tmp/absolute-path.txt`、`CON.txt`、`NUL`、`a:b*c?.txt`、`x<y>z|w.txt`）
- **步骤**：
  1. 建 `~/efv-out/safe/` 和 `~/efv-out/safe-all/`，记下 `~/efv-out/` 里现有的内容。
  2. 打开样本，点第一个附件（`../../.bashrc`）的保存图标，先看保存对话框里的默认文件名，再保存到 `~/efv-out/safe/`。
  3. 点 “Save all…” 选择 `~/efv-out/safe-all/`。
  4. 列出 `~/efv-out/safe-all/`（包含隐藏文件：macOS、Linux `ls -la`，Windows `Get-ChildItem -Force`）。
  5. 检查有没有文件跑到文件夹外面：看 `~/efv-out/` 里有没有新增的文件；macOS、Linux 上看 `ls -l ~/.bashrc` 的修改时间有没有变（或本来没有这个文件就不应出现）、`ls /tmp/absolute-path.txt` 不应存在；Windows 上看 `C:\Windows\evil.txt`、`C:\tmp\absolute-path.txt` 不应存在。
- **预期**：
  - 附件列表里显示的是邮件里的原始文件名（`../../.bashrc` 等，清理发生在保存时）；保存对话框的默认文件名里没有 `../` 之类的路径成分（第一个附件的默认名是 `.bashrc`）。
  - `safe-all/` 里**恰好 7 个文件**，名字是：`.bashrc`、`evil.txt`、`absolute-path.txt`、`_CON.txt`、`_NUL`、`a_b_c_.txt`、`x_y_z_w.txt`。
  - 所选文件夹以外没有产生任何新文件，`~/.bashrc` 没被改动。

#### ATT-06 用系统默认程序打开附件
- **规格**：email-attachments / 用系统默认程序打开附件 / 打开图片附件
- **样本**：`samples/attachments-pdf-xlsx-jpg.eml`
- **步骤**：
  1. 点 `photo.jpg` 的 “Open”（“打开”）按钮。
  2. 点 `report.pdf` 的 “Open”。
  3. 点 `budget.xlsx` 的 “Open”（机器上没有表格程序时，记下系统的反应）。
- **预期**：`photo.jpg` 在系统默认的图片查看程序里打开，显示一张 180x60 的图；`report.pdf` 在默认的 PDF 查看程序里打开；`budget.xlsx` 交给系统默认程序（没有对应程序时，由系统提示选择程序，这不是应用的问题）；应用本身不卡住、没有错误提示。

#### ATT-07 附件的“来自网络”标记
- **规格**：design.md D12（附件的“来自网络”标记；无对应规格场景）
- **样本**：`samples/attachments-pdf-xlsx-jpg.eml`（沿用 ATT-03 保存的文件和 ATT-06 打开的文件）
- **步骤**：趁着应用还开着（退出时会清理临时目录），检查**打开**出来的临时文件和**保存**出来的文件：
  - macOS：

    ```sh
    find "$TMPDIR/EmailFileViewer/sessions" -name photo.jpg -exec xattr -l {} \;
    xattr -l ~/efv-out/photo.jpg
    ```
  - Windows（PowerShell）：

    ```powershell
    Get-ChildItem "$env:LOCALAPPDATA\Temp\EmailFileViewer\sessions" -Recurse -Filter photo.jpg |
      ForEach-Object { Get-Content $_.FullName -Stream Zone.Identifier }
    Get-Content $HOME\efv-out\photo.jpg -Stream Zone.Identifier
    ```

    也可以在这两个 `photo.jpg` 上右键 → 属性 → 常规，看底部有没有“安全：此文件来自其他计算机……”和“解除锁定”（Unblock）复选框。
- **预期**：
  - macOS：两个文件都有 `com.apple.quarantine`，值形如 `0081;<十六进制时间>;EmailFileViewer;`。
  - Windows：`Zone.Identifier` 流的内容是 `[ZoneTransfer]` 和 `ZoneId=3`；属性页里有“解除锁定”。
  - 找不到临时文件时，用 `find` / `Get-ChildItem` 在用户的临时目录下搜 `photo.jpg`，并把找到的位置写进备注。
  - Linux 上没有对应机制，本项填 N/A（已预填）。

#### ATT-08 可执行和脚本类附件只能保存，不能打开
- **规格**：email-attachments / 用系统默认程序打开附件 / 打开可执行附件
- **样本**：`samples/attachments-executables.eml`（`installer.exe`、`run-me.bat`、`setup.ps1`、`install.sh`、`launcher.command`、`Tool.app`，内容都只是纯文本占位）
- **步骤**：
  1. 打开样本，看六个附件卡片。
  2. 对每一个卡片：双击它（或用 Tab 聚焦后按回车）。
  3. 在出现的对话框里点 “Cancel”（“取消”）；再试一次，这次点 “Save…”（“保存…”）保存到 `~/efv-out/exe/`。
  4. 回到附件区点 “Save all…”，保存到 `~/efv-out/exe-all/`。
- **预期**：
  - 六个卡片上都有锁形图标和 “Save only”（“仅可保存”）标记，**没有** “Open” 按钮。
  - 双击出现对话框，标题形如 “installer.exe” can't be opened here（“无法在此打开‘installer.exe’”），正文说明程序和脚本不会被打开，并建议先保存；按钮是 Cancel 和 Save…。
  - 没有任何程序被启动：没有终端或 PowerShell 窗口弹出，`launcher.command` 不会打开终端，`Tool.app` 不会被运行。
  - 点 “Save…” 能正常保存，保存出来的文件内容是一行 `PLACEHOLDER for ...` 的纯文本；“Save all…” 一次保存全部六个，通知为 “Saved 6 attachments”。
  - 对话框打开时，⌘O、⌘W、⌘, 或 Ctrl+O、Ctrl+W 不会叠出别的对话框或关掉标签页。

#### ATT-09 打开嵌套邮件：MSG 中附带另一封 MSG
- **规格**：email-attachments / 打开嵌套邮件 / MSG 中附带另一封 MSG
- **样本**：`samples/EmailWithMsgAttachment.msg`、`samples/EmailWithInnerMailAndAttachments.msg`
- **步骤**：
  1. 打开 `samples/EmailWithMsgAttachment.msg`，附件区里找到带 “Email”（“邮件”）标记的附件，点它的 “Open”（悬停提示 “Opens here, in a new level of this tab”）。
  2. 看内层邮件，然后点面包屑里的外层，或点 “Back”（“返回”）。
  3. 打开 `samples/EmailWithInnerMailAndAttachments.msg`：外层附件是 `OUTER 1.pdf`、`OUTER 2.pdf` 和一封嵌套 MSG，进入嵌套邮件，其附件是 `INNER 1.pdf`、`INNER 2.pdf`；在内层保存 `INNER 1.pdf` 到 `~/efv-out/`。
- **预期**：进入嵌套邮件后，**同一个标签页**里显示内层邮件的邮件头、正文和附件（展示方式与普通邮件相同），标签页数量不变；出现面包屑（Message levels）；返回后回到外层；内层的附件可以正常保存。

#### ATT-10 多层嵌套：继续往里打开
- **规格**：email-attachments / 打开嵌套邮件 / 多层嵌套
- **样本**：`samples/nested-rfc822-two-levels.eml`、`samples/nested-msg-two-levels.msg`
- **步骤**：
  1. 打开 `samples/nested-rfc822-two-levels.eml`（主题 `Level 1 (outer) message with two nested levels`），打开嵌套邮件进入 `Level 2 (middle) message`，再打开其嵌套邮件进入 `Level 3 (innermost) message`。
  2. 在第 3 层保存附件 `level3-note.txt`。
  3. 点面包屑回到第 1 层。
  4. 再进入第 3 层，**直接关闭这个标签页**（⌘W、Ctrl+W 或点关闭按钮）。
  5. 对 `samples/nested-msg-two-levels.msg` 重复第 1 到 3 步。
- **预期**：每一层都能继续往里打开；第 3 层有 `level3-note.txt` 且能保存；面包屑显示三层，点击可以回到任一层；窗口标题跟着当前层变；第 4 步整个标签页被关掉，没有残留的标签页，应用不崩溃。

#### ATT-11 EML 里的嵌套邮件，以及作为附件的 .eml 和 .msg
- **规格**：email-attachments / 打开嵌套邮件（EML 中的 message/rfc822、作为附件的 .eml / .msg 文件）
- **样本**：`samples/simple-embedded-message.eml`、`samples/attachment-eml-file.eml`、`samples/attachment-msg-file.eml`、`samples/TestWithAttachmentsAndInlines.eml`
- **步骤**：
  1. 打开 `samples/simple-embedded-message.eml`，打开其中的嵌套邮件。
  2. 打开 `samples/attachment-eml-file.eml`，两个附件 `forwarded-as-file.eml` 和 `forwarded-as-rfc822.eml` 都点 “Open”。
  3. 打开 `samples/attachment-msg-file.eml`，点 `forwarded-message.msg` 的 “Open”。
  4. 打开 `samples/TestWithAttachmentsAndInlines.eml`，附件区里有两个带 “Email” 标记、但内容其实不是邮件的附件，点它们的 “Open”。
- **预期**：
  - 第 1 步：显示内层邮件，主题 `embedded message`。
  - 第 2 步：两个附件都带 “Email” 标记，都能打开，主题是 `Original message that was forwarded as an attachment`。
  - 第 3 步：能打开，正文含 `RTF-ONLY-BODY`。
  - 第 4 步：出现提示 “Couldn't open <附件名> as an email”（“无法将 <附件名> 作为邮件打开”），原因是 “This attachment isn't a valid email.”，应用停留在当前这一层，没有崩溃。
  - 该邮件的 cid 图片 `Outlook-facebook.png` 不在附件列表里，4 个文本附件在。

#### ATT-12 展开 winmail.dat
- **规格**：email-attachments / 展开 winmail.dat / Outlook 发出的 winmail.dat
- **样本**：`samples/tnef-winmail-docx.eml`
- **步骤**：
  1. 打开样本，看附件区。
  2. 点 `meeting-minutes.docx` 的保存图标，保存到 `~/efv-out/`。
  3. 校验保存出来的 docx 是有效文件（机器上没有 Office 时用这个办法）：
     - macOS、Linux：`unzip -l ~/efv-out/meeting-minutes.docx`
     - Windows：`Copy-Item $HOME\efv-out\meeting-minutes.docx $HOME\efv-out\mm.zip; Expand-Archive $HOME\efv-out\mm.zip $HOME\efv-out\mm -Force; Get-ChildItem -Recurse $HOME\efv-out\mm`
  4. 点 “Open”，用系统默认程序打开它。
- **预期**：附件区显示的是 `meeting-minutes.docx`，**不是** `winmail.dat`；保存的 docx 里有 `word/document.xml`；装有 Word、Pages、LibreOffice 等程序时能打开并显示内容。

### 3.8 界面语言（app-localization）

#### LOC-01 系统语言为简体中文：首次启动显示简体中文
- **规格**：app-localization / 默认跟随系统语言 / 系统语言为简体中文
- **样本**：`samples/headers-normal.eml`
- **自动化覆盖**：语言映射规则另有测试：`ui/src/__tests__/App.test.tsx`（“场景‘系统语言为简体中文’”）、`ui/src/settings/__tests__/language.test.ts` 和 Rust 的 `src-tauri/src/settings.rs` 里的测试。这里验证真实系统的读取。
- **步骤**：
  1. 完全退出应用，按 1.6 重置设置。
  2. 把系统首选语言设成简体中文：
     - Windows：设置 → 时间和语言 → 语言和区域，添加“中文(简体，中国)”并设为 Windows 显示语言，**注销再登录**（需要联网下载语言包；耗时较长，Windows 10 1809 可以只做 Windows 11，另一台填 N/A 并写明）。
     - macOS：系统设置 → 通用 → 语言与地区，把“简体中文”拖到首选语言的最上面（会提示重启）。
     - Linux：从终端启动，环境变量决定语言：`LC_ALL=zh_CN.UTF-8 LANG=zh_CN.UTF-8 LANGUAGE=zh_CN emailfileviewer`（AppImage 同理，命令换成 AppImage 文件）。不要从图标启动，图标启动读不到这几个变量。
  3. 启动应用，看空状态，再打开 `samples/headers-normal.eml`。
- **预期**：界面是简体中文：空状态标题 “打开邮件文件”，按钮 “打开文件…”，隐私说明 “邮件只在本机读取……”；邮件头标签是“发件人”“收件人”“抄送”“日期”；macOS 的菜单栏是中文（“文件”“编辑”“窗口”）。
- **补充（可选）**：系统语言为繁体中文（`zh-TW`）时应显示英文。

#### LOC-02 系统语言为德语：首次启动显示英文
- **规格**：app-localization / 默认跟随系统语言 / 系统语言为德语
- **样本**：`samples/headers-normal.eml`
- **自动化覆盖**：同 LOC-01，测试名为“场景‘系统语言为德语’”。
- **步骤**：同 LOC-01，语言换成 Deutsch (Deutschland)。Linux 的启动命令：`LC_ALL=de_DE.UTF-8 LANG=de_DE.UTF-8 LANGUAGE=de_DE emailfileviewer`。
- **预期**：界面是英文：空状态标题 “Open an email file”，邮件头标签 “From”“To”“Cc”“Date”。

#### LOC-03 手动切换语言：立即生效，重启后保留
- **规格**：app-localization / 手动切换语言 / 切换为英文并重启
- **样本**：`samples/headers-normal.eml`
- **步骤**：
  1. 在中文界面下打开 `samples/headers-normal.eml`，打开设置（工具栏齿轮；macOS 可按 ⌘,）。
  2. 在 “语言”（Language）里选 “English”。观察整个界面，不要重启。
  3. 完全退出应用，再启动，打开设置查看语言，再打开一个文件。
  4. 切回 “简体中文”，再退出重启一次。
- **预期**：
  - 第 2 步：设置对话框、工具栏、邮件头标签、附件区等的文字**立即**变成英文，不需要重启；语言选项始终用各自的语言书写（English、简体中文）。
  - 第 3 步：重启后仍是英文，从第一帧起就是英文（不会先中文再英文）。
  - 第 4 步：重启后是简体中文。
  - macOS 的菜单栏文字**要到下次启动才更新**，这是设计内的行为，不算失败。

#### LOC-04 界面语言不改变邮件内容
- **规格**：app-localization / 界面语言不改变邮件内容 / 中文界面查看英文邮件
- **样本**：`samples/body-html-and-plain.eml`、`samples/attachments-pdf-xlsx-jpg.eml`、`samples/charset-gbk.eml`
- **步骤**：
  1. 在简体中文界面下打开 `samples/body-html-and-plain.eml` 和 `samples/attachments-pdf-xlsx-jpg.eml`。
  2. 切换到英文界面，再看一遍。
  3. 在英文界面下打开 `samples/charset-gbk.eml`。
- **预期**：中文界面里按钮和标签是中文，邮件主题 `HTML and plain-text alternatives`、正文 `HTML-VERSION`、附件名 `report.pdf`、`budget.xlsx`、`photo.jpg` 保持原样；切换语言前后邮件内容一致；英文界面里的 GBK 邮件正文仍然是中文。日期格式随界面语言变化（见 VIEW-03），这是允许的。

### 3.9 外观（app-appearance）

#### APPR-01 首次启动跟随系统：系统是深色时界面是深色
- **规格**：app-appearance / 明暗模式 / 首次启动跟随系统
- **样本**：`samples/headers-normal.eml`
- **步骤**：
  1. 完全退出应用，按 1.6 重置设置。
  2. 把系统设成深色模式（1.7）。
  3. 启动应用，打开 `samples/headers-normal.eml`，再打开设置看 “Mode”（“模式”）和 “Colour scheme”（“配色方案”）。
- **预期**：界面是深色；设置里 “Match system”（“跟随系统”）被选中，默认配色方案是 “Verdigris”（“铜绿”）；窗口标题栏（Windows、macOS）也是深色。

#### APPR-02 运行中系统切换外观：界面立即跟随
- **规格**：app-appearance / 明暗模式 / 运行中系统切换外观
- **样本**：`samples/headers-normal.eml`
- **步骤**：应用保持在 “Match system”，邮件已打开。**不重启应用**，先把系统从深色切到浅色，再切回深色。
- **预期**：每次切换后界面（包括窗口底色和标题栏）在约 1 秒内跟着变，无需重启，没有残留的错色区域。Linux 上这条依赖 WebKitGTK 能否收到系统外观变化，若不跟随，记 ❌ 并写明桌面环境。

#### APPR-03 手动固定为浅色（或深色）：不再受系统影响
- **规格**：app-appearance / 明暗模式 / 手动固定为浅色
- **样本**：`samples/headers-normal.eml`
- **步骤**：
  1. 系统保持深色，在设置里选 “Light”（“浅色”）。
  2. 把系统切到浅色再切回深色。
  3. 反过来：设置选 “Dark”（“深色”），系统切到浅色。
- **预期**：选浅色后，系统切换时界面始终是浅色；选深色后，系统是浅色时界面仍是深色。窗口标题栏（Windows、macOS）跟着所选模式。

#### APPR-04 切换配色方案：立即生效，明暗模式不变
- **规格**：app-appearance / 预设配色方案 / 切换配色方案
- **样本**：`samples/headers-normal.eml`
- **步骤**：设置 → “Colour scheme”（“配色方案”），依次选每一套，每选一套都看界面。模式保持不变。
- **预期**：共 5 套：Verdigris（铜绿，默认）、Graphite（石墨）、Indigo（靛蓝）、Plum（梅紫）、High contrast（高对比度）；每选一套界面立即换色，当前的明暗模式不变；所选的一套有 “Selected”（“已选择”）标记。

#### APPR-05 配色方案与明暗模式组合：每套都有明暗两种变体，正文始终是浅色底
- **规格**：app-appearance / 预设配色方案 / 配色方案与明暗模式组合；外观不改变邮件内容
- **样本**：`samples/marketing-remote-content.eml`、`samples/body-plain-only.eml`
- **步骤**：打开这两个文件。把模式固定为 Dark，依次选 5 套配色，每套看两封邮件；再把模式固定为 Light，重复一遍。按下表记录。
- **预期**：每个单元格都应是“是”。

  | 配色方案 | 浅色下界面是该方案的浅色变体 | 深色下界面是该方案的深色变体 | 深色下邮件正文仍是浅色底 |
  |----------|-----------------------------|------------------------------|--------------------------|
  | Verdigris | | | |
  | Graphite | | | |
  | Indigo | | | |
  | Plum | | | |
  | High contrast | | | |

  “界面是该方案的深色变体”是指：界面背景是深色，强调色和该方案一致（例如 Verdigris 是青绿色、Indigo 是靛蓝色）。

#### APPR-06 重启后保持外观设置
- **规格**：app-appearance / 外观设置持久保存 / 重启后保持设置
- **样本**：`samples/headers-normal.eml`
- **步骤**：设置里选 “Dark” 和 “Plum”，完全退出应用，再启动。
- **预期**：应用以深色和 Plum 配色启动，从第一帧起就是（没有先闪一下默认配色或浅色）；设置里这两项处于选中状态。再选 “Light” 和 “Graphite” 重启一次，结果相同。

#### APPR-07 所有配色的可读性（对比度）
- **规格**：app-appearance / 可读性 / 检查全部配色
- **样本**：无
- **自动化覆盖**：由自动化测试覆盖，不需要手动填写。对比度检查脚本 `ui/scripts/check-contrast.ts`（`cd ui && npm run contrast`）逐套方案、逐种模式校验文字与背景不低于 4.5:1、大号文字和图标不低于 3:1，在 CI 的 `.github/workflows/ui.yml` 中运行；测试 `ui/src/theme/__tests__/theme.test.ts` 也引用同一套计算。APPR-05 里人眼看一遍 High contrast 的两种变体即可，不单独记结果。

#### APPR-08 深色模式下查看 HTML 邮件：正文仍按邮件自身样式显示在浅色底上
- **规格**：app-appearance / 外观不改变邮件内容 / 深色模式下查看 HTML 邮件
- **样本**：`samples/marketing-remote-content.eml`、`samples/body-html-and-plain.eml`、`samples/HtmlSampleEmailWithAttachment.msg`
- **步骤**：模式固定为 Dark，依次打开三个文件。
- **预期**：应用界面是深色；三封邮件的正文都显示在浅色底上，按邮件自己的样式显示（营销邮件的 600 像素版式和自带背景色、深色文字），没有被反色、没有“深色正文套深色背景”导致看不清的地方。

#### APPR-09 深色模式下查看纯文本邮件：深色文字在浅色底上
- **规格**：app-appearance / 外观不改变邮件内容 / 深色模式下查看纯文本邮件
- **样本**：`samples/body-plain-only.eml`、`samples/TxtSampleEmail.msg`
- **步骤**：模式固定为 Dark，依次打开两个文件。
- **预期**：应用界面是深色，邮件正文是**深色文字显示在浅色底上**，文字清晰可读；选中文字时高亮也清晰。

## 4. 安全与隐私验收（10.2）

对应 tasks.md 10.2，以及 5.7 并入这里的实验：Windows 和 Linux 上，邮件正文里的链接、锚点、新窗口请求的行为是否与已验证过的 macOS 一致（design.md 风险部分）。

### 4.0 这一章能证明什么，不能证明什么

- 发布版里开发者工具是关闭的，看不到邮件正文 iframe 里的脚本状态，所以这里**只能看行为**：页面上的标记文字有没有被改、有没有弹窗、有没有新窗口、有没有多出的标签页或文件、有没有网络请求。
- 邮件正文有两道防线：sidecar 在解析时就删掉脚本、事件属性和 `javascript:` 链接；正文放进沙箱 iframe（`sandbox="allow-popups allow-popups-to-escape-sandbox"`，没有 `allow-scripts`、`allow-same-origin`、`allow-forms`），并带默认严格的 CSP。在发布版里做恶意样本测试，通过只能说明两道防线合起来有效，**不能单独证明沙箱这一层**。
- 沙箱和 IPC 隔离的分层验证（design.md 记录的 Windows 上初始化脚本会进入所有子框架，Windows 和 Linux 的 WebView 对 sandbox、srcdoc、CSP 的实现差异）需要改动代码的开发构建，不在这份人工验收里做。macOS 上的分层实验结果已写在 design.md 的风险部分。
- 另有自动化测试守着这些字符串和规则：iframe 的 sandbox 属性与两种 CSP 字符串（`ui/src/viewer/__tests__/bodyDocument.test.ts`）、HTML 清理规则（`core/tests/EmailFileViewer.Core.Tests/HtmlSanitizerTests.cs`、`HtmlProcessingSampleTests.cs`）、capabilities 只授予主窗口的本地来源（`src-tauri/capabilities/default.json`）。

### 4.1 样本里会被访问的远程地址

抓包时按这些主机名搜索（来自样本内容，样本里的其余地址都只是文字）：

| 主机名 | 出现在 | 说明 |
|--------|--------|------|
| `raw.githubusercontent.com` | `samples/marketing-remote-content.eml` | 远程图片、追踪像素、样式表 |
| `fonts.googleapis.com`、`fonts.gstatic.com` | `samples/marketing-remote-content.eml` | 远程字体（路径是虚构的，只用来被阻止） |
| `efv-probe.invalid` | `samples/html-malicious.eml` | 恶意样本的探测地址（`.invalid` 永远解析不了，不会真的访问） |
| `example.com` | `samples/marketing-remote-content.eml`、`samples/html-malicious.eml`、`samples/link-text-mismatch.eml`、`samples/links-anchors-and-schemes.eml`、`samples/body-html-and-plain.eml`、`samples/body-plain-only.eml` | 链接目标，不点击就不应有请求 |
| `www.paypal.com` | `samples/link-text-mismatch.eml` | 伪造的链接文字 |
| `www.facebook.com`、`che01.safelinks.protection.outlook.com` | `samples/TestWithAttachmentsAndInlines.eml` | 链接目标 |
| `www.startssl.com` | `samples/smime-signed-thunderbird.eml` | 证书里的吊销检查地址：出现请求说明在做证书链验证 |

### 4.2 抓包方法

每个平台给一种，**目的是看应用有没有发出请求**。

通用做法：

1. 关掉浏览器、同步客户端、系统更新等会联网的程序，机器保持联网。
2. 开始抓包，应用启动到空状态，**什么都不做静置 60 秒**，这是基线：WebView 自己可能有少量背景连接（更新检查、遥测），它们不算。
3. 记下开始操作的时刻（`date`），执行检查项里的操作。
4. 判定：操作之后出现 (a) 任何 DNS 查询或 TLS 握手（SNI）里含 4.1 的主机名，或 (b) 应用进程和 WebView 进程发起了基线里没有的外部连接，就是失败。

**macOS**（macOS 自带的 `tcpdump` 能显示进程名）

```sh
# 终端 1：抓全部接口（含回环），需要管理员密码，Ctrl+C 结束
sudo tcpdump -i pktap,all -n -w ~/efv-capture.pcapng

# 结束后分析。第一条按进程名找应用和 WebKit 的流量，第二条按主机名找 DNS 和 TLS 握手
tcpdump -n -k NP -r ~/efv-capture.pcapng 2>/dev/null | grep -i -E 'emailfileviewer|efv-sidecar|WebKit'
tcpdump -n -A -r ~/efv-capture.pcapng 2>/dev/null | grep -a -i -E 'githubusercontent|googleapis|gstatic|efv-probe|example|paypal|facebook|safelinks|startssl'

# 连接快照（打开样本之后立刻执行）
sudo lsof -i -n -P | grep -i -E 'emailfileviewer|efv-sidecar|WebKit'
```

- macOS 上 DNS 查询由系统的 `mDNSResponder` 代发，进程名不会是本应用，所以 **DNS 必须用主机名搜索**，不能只按进程名。
- 系统配置了加密 DNS（DoH、DoT）时抓不到主机名，先临时关掉。
- 不想用终端时可以用“活动监视器”：菜单“显示 → 所有进程”，切到“网络”页，在搜索框分别搜 `EmailFileViewer` 和 `WebKit`，对比操作前后“发送的字节”和“接收的字节”是否增长。

**Windows**（Wireshark）

1. 安装 Wireshark（含 Npcap），选当前联网的网卡开始抓包。
2. 显示过滤器（按主机名找）：

   ```text
   frame matches "(?i)githubusercontent|googleapis|gstatic|efv-probe|example|paypal|facebook|safelinks|startssl"
   ```

   再单独用过滤器 `dns` 看所有 DNS 查询，和基线对照。
3. 进程层面用“资源监视器”（运行 `resmon`）→ “网络”页：在“具有网络活动的进程”里勾选 `emailfileviewer.exe`、`efv-sidecar.exe` 和属于本应用的 `msedgewebview2.exe`，下面的“网络活动”“TCP 连接”只显示它们的流量。
4. 找出属于本应用的 WebView2 进程，并取连接快照（PowerShell，打开样本之后立刻执行）：

   ```powershell
   $webview = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" |
     Where-Object CommandLine -like '*com.github.realzony.emailfileviewer*'
   $ids = @((Get-Process emailfileviewer, efv-sidecar -ErrorAction SilentlyContinue).Id) + @($webview.ProcessId)
   Get-NetTCPConnection -OwningProcess $ids -ErrorAction SilentlyContinue |
     Where-Object { $_.RemoteAddress -notin '127.0.0.1', '::1', '0.0.0.0', '::' } |
     Select-Object OwningProcess, State, RemoteAddress, RemotePort
   ```

   不能装 Wireshark 时，就用第 3、4 步，再补充看 `Get-DnsClientCache | Where-Object Entry -match 'githubusercontent|googleapis|gstatic|efv-probe'`（操作前先 `ipconfig /flushdns`）。DNS 缓存只是辅助，WebView2 可能使用自己的解析器，不能单独作为通过的依据。

**Linux**

```sh
# 抓全部接口（含回环），Ctrl+C 结束
sudo tcpdump -i any -n -w ~/efv-capture.pcap

# 所有 DNS 查询（含 systemd-resolved 的本地存根 127.0.0.53）
tcpdump -n -r ~/efv-capture.pcap 'port 53 or port 853 or port 5353'
# 按主机名搜 DNS 和 TLS 握手
tcpdump -n -A -r ~/efv-capture.pcap 2>/dev/null | grep -a -i -E 'githubusercontent|googleapis|gstatic|efv-probe|example|paypal|facebook|safelinks|startssl'

# 连接快照（打开样本之后立刻执行）
sudo ss -tunap | grep -i -E 'emailfileviewer|efv-sidecar|WebKit'
```

- tcpdump 本身不能按进程过滤，所以靠主机名搜索和 `ss` 快照。
- Linux 上应用的 DNS 请求会发往本机的存根解析器（`127.0.0.53`），`-i any` 能抓到，所以**断网状态下也能看到应用有没有试图解析域名**。

### 4.3 恶意 HTML 样本

#### SEC-01 恶意 HTML：脚本都不执行，正文其余部分正常显示
- **规格**：email-viewing / 正文在沙箱中渲染 / 正文包含脚本
- **样本**：`samples/html-malicious.eml`（含 `<script>`、`onerror`、`onload`、`onclick`、`onmouseover`、`onfocus`、`javascript:` 链接、`<form>`、`<meta http-equiv="refresh">`、`<iframe>`、`<object>`、`<embed>`、`<base>` 等，探测都只调用不存在的命令 `efv_probe_should_fail`，地址都是 `efv-probe.invalid`）
- **步骤**：
  1. 先记下当前窗口标题、标签页数量和窗口数量。
  2. 打开样本，**不要**点 “Load remote content”。
  3. 观察正文，等待 10 秒（`meta refresh` 如果生效会在这段时间内跳走）。
  4. 在正文里依次：点击 “Event attributes on a div” 那一行、把鼠标移到它上面、点一下 `autofocus input` 输入框、点击两个 `javascript:` 链接。
  5. 每一步之后看一眼窗口标题、标签页、有没有弹窗。
- **预期**：
  - 页面上能看到三处标记：`MARKER-START: the rest of the body must still be shown.`、`NO-SCRIPT-RAN`（**不是** `SCRIPT-RAN: ...`，后者说明脚本执行了）、`MARKER-END: reached the end of the body.`。第 4 步之后 `NO-SCRIPT-RAN` 仍然不变。
  - 没有任何弹窗（alert、对话框、权限请求），没有新窗口，没有新标签页，没有任何程序被启动，系统浏览器没有被拉起。
  - 窗口标题保持 `Malicious HTML probe (harmless) — EmailFileViewer`，不会变成邮件正文里 `<title>` 的 `Malicious HTML probe`，也不会变成别的内容。
  - 页面没有被 `meta refresh` 或 `<base>` 带走，正文一直在。
  - 应用自己的界面（标签栏、工具栏、附件区）都没有被改动。
  - 正常的 https 链接 “A normal https link that should keep working” 保留着；点击它在系统浏览器里打开 `https://example.com/ok`。

#### SEC-02 恶意 HTML：调用不到应用的内部接口，应用状态和用户文件不受影响
- **规格**：email-viewing / 正文在沙箱中渲染 / 正文尝试调用应用接口
- **样本**：`samples/html-malicious.eml`（里面的脚本会尝试 `window.__TAURI_INTERNALS__.invoke(...)`、`window.__TAURI__.core.invoke(...)` 和 `window.parent.postMessage(...)`）
- **步骤**：
  1. 在 SEC-01 的基础上，同时打开另外 2 封正常邮件（例如 `samples/headers-normal.eml`、`samples/body-plain-only.eml`），记下目前的标签页。
  2. 先记录“副作用基线”：`~/efv-out/`、桌面、下载目录和用户目录里最近修改的文件（macOS、Linux：`ls -lt ~ ~/Desktop ~/Downloads | head -20`；Windows：`Get-ChildItem $HOME, $HOME\Desktop, $HOME\Downloads | Sort-Object LastWriteTime -Descending | Select-Object -First 20`），以及 `efv-sidecar` 的进程数。
  3. 在 `samples/html-malicious.eml` 的标签页里停留 30 秒，做 SEC-01 第 4 步的交互。
  4. 回头对照第 2 步的清单。
- **预期**：
  - 没有弹出过任何文件对话框（打开、保存、选择文件夹），没有新增标签页，其他标签页的内容和顺序不变。
  - 没有出现新文件（用户目录、桌面、下载、应用的临时目录里都没有新增内容），`efv-sidecar` 进程数没变。
  - 页面上没有出现 `efv_probe_should_fail` 之类的错误文字或回显。
  - 应用在整个过程中可以正常使用。
  - 调用是否真的被拒绝无法在发布版里直接观察，见 4.0。

#### SEC-03 放开远程内容之后，脚本仍然不执行
- **规格**：email-viewing / 正文在沙箱中渲染（放宽 CSP 后沙箱仍然有效）
- **样本**：`samples/html-malicious.eml`
- **步骤**：打开样本，点提示条上的 “Load remote content”（“加载远程内容”）。重复 SEC-01 的第 3、4 步。
- **预期**：`NO-SCRIPT-RAN` 仍然不变，没有弹窗、没有新窗口、没有被带走；窗口标题不变。（此时样本里的远程样式表会去访问 `efv-probe.invalid`，它永远解析不了，所以没有真实的网络连接。）

### 4.4 远程内容

#### SEC-04 远程内容默认不加载
- **规格**：email-viewing / 远程内容默认不加载 / 营销邮件中的远程图片
- **样本**：`samples/marketing-remote-content.eml`
- **步骤**：打开样本，**不要**点任何东西，观察正文和正文上方。
- **预期**：
  - 正文上方有提示条：标题 “Remote images and styles are blocked.”（“已阻止远程图片和样式。”），说明 “Loading them lets the sender see that you opened this message.”，有 “Load remote content”（“加载远程内容”）按钮和 “This message only”（“仅对此邮件生效”）标记。
  - 远程图片都没有显示（logo、横幅、3 张商品图、2 个社交图标、追踪像素都不加载），标题**不是红色**（`marketing.css` 没有生效）；版式和文字仍然可读，链接还在。

#### SEC-05 用户选择加载：只对当前这封邮件生效
- **规格**：email-viewing / 远程内容默认不加载 / 用户选择加载
- **样本**：`samples/marketing-remote-content.eml`、`samples/html-malicious.eml`
- **步骤**（建议联网，能看到图片）：
  1. 依次打开 `samples/marketing-remote-content.eml`（标签页 1）、`samples/html-malicious.eml`（标签页 2）、再打开一次 `samples/marketing-remote-content.eml`（标签页 3）。
  2. 回到标签页 1，点 “Load remote content”。
  3. 切到标签页 2、标签页 3，看提示条。再切回标签页 1。
  4. 关掉标签页 1，再重新打开 `samples/marketing-remote-content.eml`。
- **预期**：
  - 第 2 步之后，标签页 1 的提示条变成 “Remote content is loaded for this message. Other messages still block it.”（“已为此邮件加载远程内容。其他邮件仍默认阻止。”）；样本素材已在 `main` 上时，远程图片显示出来、标题变红；素材还没合并到 `main` 时图片是裂图，属于预期（`samples/README.md` 的“缺口与局限”第 5 条），此时以提示条的变化和 SEC-08 的抓包为准。
  - 标签页 2、标签页 3 的提示条仍是 “blocked” 状态。
  - 回到标签页 1，仍是已加载状态。
  - 第 4 步重新打开后是阻止状态（v1 没有“重新阻止”按钮，关掉标签页再打开就恢复）。

### 4.5 断网与抓包

#### SEC-06 断网状态下打开全部样本
- **规格**：email-viewing / 查看时不产生网络请求 / 离线打开邮件
- **样本**：`samples/` 根目录下全部 50 个 `.eml`/`.msg`，以及 `samples/invalid/` 下的 5 个文件
- **步骤**：
  1. 让机器完全离线。虚拟机在虚拟化软件里断开网卡最干净；真机关闭 Wi-Fi 并拔掉网线。用 `ping -c 1 1.1.1.1`（Windows：`ping -n 1 1.1.1.1`）确认不通，浏览器也打不开网页。Linux 上可以同时开着 4.2 的抓包。
  2. 启动应用，用文件对话框打开 `samples/` 根目录下的全部文件（对话框里 Ctrl 或 ⌘ 加 A 全选；默认筛选器只显示 `.eml` 和 `.msg`）。
  3. 用左右方向键依次浏览每个标签页，检查邮件头和正文有内容，没有错误标签页，没有一直转圈。
  4. 再把筛选器切到 “All files”，打开 `samples/invalid/` 下的 5 个文件。
  5. 恢复网络。
- **预期**：
  - 根目录的 50 个样本全部正常显示，**没有错误标签页**，没有卡住，没有任何联网失败的提示。
  - `samples/invalid/` 里 `not-an-email.png`、`png-disguised-as-eml.eml`、`empty.eml` 是 “Unsupported format” 错误标签页，`truncated.msg` 是 “Damaged file” 错误标签页，`truncated-half.eml` 正常打开。
  - 营销邮件在离线时显示提示条，图片没有显示。
  - 全部过程应用功能不受影响，响应速度没有明显变慢（如果每个邮件都等几秒才出现，说明在等网络超时，记为失败）。

#### SEC-07 联网抓包：未允许远程内容时，应用没有任何网络请求（包括 DNS）
- **规格**：email-viewing / 查看时不产生网络请求；远程内容默认不加载
- **样本**：`samples/` 根目录下全部 50 个 `.eml`/`.msg`、`samples/invalid/` 下的 5 个文件；重点是 `samples/marketing-remote-content.eml`、`samples/html-malicious.eml`、`samples/link-text-mismatch.eml`、`samples/links-anchors-and-schemes.eml`、`samples/smime-signed-thunderbird.eml`
- **为什么要联网做**：机器完全离线时，DNS 解析和连接在离开本机之前就失败了，抓包看不到应用有没有“试过”；只有联网，任何一次试探才会真的出现在抓包里。SEC-06 验证“离线可用”，本项验证“不会发出请求”。
- **步骤**：
  1. 联网，按 4.2 开始抓包，应用启动到空状态，静置 60 秒（基线），记下时刻 T0。
  2. 一次打开 `samples/` 根目录下的全部 50 个文件，再打开 `samples/invalid/` 下的 5 个文件。
  3. 逐个浏览标签页。在 `samples/marketing-remote-content.eml`、`samples/link-text-mismatch.eml` 和 `samples/links-anchors-and-schemes.eml` 的各个链接上各悬停几秒（检查没有 DNS 预解析）。**不要点链接，也不要点 “Load remote content”**（浏览器的流量会混进来）。
  4. 对 `samples/smime-*.eml`、`samples/smime-*.msg`（共 7 个，含 `smime-signed-thunderbird.eml`）再逐个看一遍。
  5. 再等 30 秒，停止抓包，按 4.2 分析，并在停止前取一次连接快照。
- **预期**：
  - T0 之后，抓包里没有任何 DNS 查询或 TLS 握手含 4.1 表中的主机名。
  - 应用主程序、`efv-sidecar` 和 WebView 进程没有基线之外的外部连接。
  - 尤其是 `smime-signed-thunderbird.eml` 没有触发向 `www.startssl.com`（证书的 CRL、OCSP 地址）的请求。
  - 基线里出现的 WebView 背景连接（如果有）记在“现象”里，说明目的地址，不算失败。

#### SEC-08 点“加载远程内容”之后才出现请求，而且只对当前这封邮件生效
- **规格**：email-viewing / 远程内容默认不加载 / 用户选择加载；查看时不产生网络请求
- **样本**：`samples/marketing-remote-content.eml`、`samples/html-malicious.eml`
- **步骤**：
  1. 联网，开始抓包；应用打开 `samples/marketing-remote-content.eml`（标签页 1）和 `samples/html-malicious.eml`（标签页 2）。
  2. 在两个标签页上都不操作，等 20 秒，记下时刻 T1。
  3. 回到标签页 1，点 “Load remote content”，等 20 秒，记下时刻 T2。
  4. 切到标签页 2（保持阻止状态），等 20 秒，记下时刻 T3。
  5. 停止抓包，按时间戳分段分析 T1 之前、T1 到 T2、T2 到 T3。
- **预期**：
  - T1 之前（点击之前）没有任何请求和 DNS 查询。
  - 点击之后出现对 `raw.githubusercontent.com`（图片、追踪像素、样式表）以及 `fonts.googleapis.com`、`fonts.gstatic.com` 的 DNS 查询和 TLS 连接。远程资源还没合并到 `main` 时服务器会返回 404，请求照样会出现。
  - **整个过程中没有** `efv-probe.invalid` 的请求（`html-malicious.eml` 没有被允许），也没有 `example.com`（没有点链接）。
  - 标签页 2 在整个过程中保持阻止状态。

### 4.6 Windows 和 Linux 上的链接、锚点、新窗口拦截（任务 5.7 的实验）

这一节要回答：WebView2（Windows）和 WebKitGTK（Linux）上，邮件正文里的链接、页内锚点和新窗口请求，行为是否与 macOS 一致。结论要写进 design.md 的风险部分。

- LINK-01（https 链接在系统浏览器中打开、应用里没有新窗口）和 LINK-03（页内锚点能滚动）是核心实验，请在 Windows 和 Linux 上认真做，并把结果与 macOS 的对照。
- 下面三项都用 `samples/links-anchors-and-schemes.eml` 测各种链接，每个链接的预期行为也写在 `samples/README.md` 的“沙箱、链接与安全”表里。macOS 也建议做一遍，作为对照。
- 如果某个平台上点链接会在应用里新开窗口、或者把正文换成别的内容（拦截无效），按设计需要启用退路：链接不可点击，显示实际地址并提供复制。这种情况请立刻在第 6 章登记。

#### SEC-09 http、mailto 以及 SVG 和图像映射里的外部链接交给系统，应用里没有新窗口
- **规格**：design.md D5（只放行 `http`、`https`、`mailto`，交给系统默认程序）
- **样本**：`samples/links-anchors-and-schemes.eml`
- **步骤**：打开样本，依次点：
  1. “Link kinds” 一节里的 “https link”、“http link”、“mailto link”；
  2. “SVG links” 一节里的 “SVG link to https://example.com/svg (href)”；
  3. “Image map” 一节里那张图片（它有一个覆盖整张图的可点击区域）。

  每点一个，看系统的反应和应用，再回到应用点下一个。
- **预期**：
  - https 链接、http 链接、SVG 里的链接、图像映射区域分别在系统默认浏览器里打开 `https://example.com/page`、`http://example.com/plain`、`https://example.com/svg`、`https://example.com/area`（http 地址被浏览器自动升级成 https 也可以）。
  - mailto 链接交给系统处理（打开默认邮件程序的新邮件窗口，或系统弹出选择程序的提示；机器上没有邮件程序时系统可能没有反应，把系统的反应写进备注）。
  - 每一次点击之后，应用里**没有新窗口**，正文还在，窗口标题（`Links: anchors and schemes — EmailFileViewer`）和标签页不变。

#### SEC-10 tel 和 file 链接不起作用
- **规格**：design.md D5（拒绝在应用内创建窗口，不放行其他协议）
- **样本**：`samples/links-anchors-and-schemes.eml`
- **步骤**：在 “Link kinds” 一节依次点 “tel link” 和 “file link”。每点一个看一眼应用和系统。
- **预期**：两个都**没有任何反应**：没有程序被启动，没有对话框，没有新窗口，正文和窗口标题不变。（`file:` 链接在解析时已经被清理成不可点击；`tel:` 链接被应用拦截。）

#### SEC-11 相对链接和空链接不会毁掉正文
- **规格**：design.md D5 / 风险（相对地址按应用首页解析，点击后 iframe 可能被换成应用自己的页面）
- **样本**：`samples/links-anchors-and-schemes.eml`
- **步骤**：在 “Link kinds” 一节依次点 “relative link: page.html”、“relative link: ?x=1”、“relative link: /root”、“empty link: href=""”。每点一个都看正文。
- **预期**：每个都**没有明显反应**，正文始终还在，没有被换成应用的界面或空白页，没有新窗口，没有被拉起的浏览器标签页，窗口标题不变。（空 `href` 在解析时已经被去掉，那行文字不是链接。）Windows 上相对链接会被解析成 `http://tauri.localhost/...`，也应该被拦住，不能交给系统浏览器。

## 5. 安装与卸载（9.6）

对应 tasks.md 9.6：在 Windows 和通过 deb 安装的 Ubuntu 上卸载后，应用文件被删除，且 `.eml` / `.msg` 不再关联本应用。

macOS 删除 `.app` 即卸载，Fedora 的 AppImage 删除文件即可，两者不在规格范围内，不设检查项。Debian 13 也是 deb 安装，顺带一起做。

#### UNINST-01 Windows 卸载：应用文件被删除，文件关联被移除
- **规格**：app-distribution / 卸载 / Windows 上卸载
- **样本**：`samples/headers-normal.eml`、`samples/body-rtf-only.msg`
- **步骤**：
  1. 在做完第 3、4 章其他项之后再做这一项，先给虚拟机打快照。确认应用已完全退出。
  2. 记录卸载前的状态：

     ```powershell
     Test-Path "$env:LOCALAPPDATA\EmailFileViewer"          # 用 1.4 记下的实际安装路径
     reg query "HKCU\Software\Classes\.eml" /ve
     reg query "HKCU\Software\Classes\.msg" /ve
     ```

     并在资源管理器里右键 `samples/headers-normal.eml` → 打开方式，确认列表里有 EmailFileViewer。
  3. 卸载：
     - Windows 11：设置 → 应用 → 已安装的应用 → EmailFileViewer → 右侧三个点 → 卸载。
     - Windows 10 1809：设置 → 应用 → 应用和功能 → EmailFileViewer → 卸载。

     按卸载向导操作。向导里如果有“删除应用程序数据”（Delete the application data）选项，第一次保持不勾选，并在备注里写明有没有这个选项。
  4. 卸载后检查：

     ```powershell
     Test-Path "$env:LOCALAPPDATA\EmailFileViewer"          # 预期 False
     Get-ChildItem "$env:APPDATA\Microsoft\Windows\Start Menu\Programs" -Recurse -Filter '*EmailFileViewer*'   # 预期无输出
     Get-Process emailfileviewer, efv-sidecar -ErrorAction SilentlyContinue                                    # 预期无输出
     reg query "HKCU\Software\Classes" /s /f EmailFileViewer
     reg query "HKLM\Software\Classes" /s /f EmailFileViewer
     reg query "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall" /s /f EmailFileViewer
     ```
  5. 检查文件关联：
     - 设置 → 应用 → 默认应用：Windows 11 在搜索框“输入文件类型或链接类型”里分别输入 `.eml`、`.msg`；Windows 10 1809 点“按文件类型选择默认应用”，在列表里找 `.eml`、`.msg`。
     - 右键 `samples/headers-normal.eml` 和 `samples/body-rtf-only.msg` → 打开方式，看列表。
     - 双击 `samples/headers-normal.eml`。
- **预期**：
  - 安装目录不存在；开始菜单快捷方式不存在；“应用”列表里没有 EmailFileViewer；没有残留进程。
  - 三条 `reg query` 都显示 `End of search: 0 match(es) found.`（没有任何指向 EmailFileViewer 的注册表项）。
  - `.eml`、`.msg` 的默认应用不是 EmailFileViewer（可能显示其他程序，或“选择默认值”）；“打开方式”列表里没有它；双击不会启动 EmailFileViewer。
  - 用户数据（`%APPDATA%\com.github.realzony.emailfileviewer` 里的 `settings.json`、`%LOCALAPPDATA%\com.github.realzony.emailfileviewer` 里的 WebView2 数据）按向导里的选项处理：未勾选“删除应用程序数据”时保留是正常的，不算失败；勾选了却还在，记为失败。
  - 如果你之前手动选过“始终使用此应用”，`HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.eml\UserChoice` 里可能还留着指向已删除程序的记录，这是 Windows 自身的行为，写进备注，不算失败。

#### UNINST-02 deb 卸载：应用文件被删除，文件关联被移除
- **规格**：app-distribution / 卸载（通过 deb 安装的 Linux）
- **样本**：`samples/headers-normal.eml`、`samples/body-rtf-only.msg`
- **步骤**：
  1. 在做完第 3、4 章其他项之后再做这一项。确认应用已完全退出。
  2. 记录卸载前的状态（**包名**用 deb 文件查）：

     ```sh
     PKG=$(dpkg-deb -f EmailFileViewer_<版本>_amd64.deb Package); echo "$PKG"
     dpkg -L "$PKG" | tee ~/efv-files.txt
     dpkg -L "$PKG" | grep -E '\.desktop$'
     xdg-mime query default message/rfc822
     xdg-mime query default application/vnd.ms-outlook
     gio mime message/rfc822
     grep -i emailfileviewer /usr/share/applications/mimeinfo.cache
     ```

     记下输出。
  3. 卸载：

     ```sh
     sudo apt remove "$PKG"
     ```
  4. 卸载后检查：

     ```sh
     dpkg -l "$PKG"                       # 预期不是 ii（not found 或 rc）
     which emailfileviewer                # 预期无输出
     while read -r f; do [ -f "$f" ] && echo "STILL THERE: $f"; done < ~/efv-files.txt   # 预期无输出
     ls /usr/share/applications | grep -i emailfileviewer                                  # 预期无输出
     grep -i emailfileviewer /usr/share/applications/mimeinfo.cache                        # 预期无输出
     gio mime message/rfc822
     xdg-mime query default message/rfc822
     xdg-mime query default application/vnd.ms-outlook
     ```
  5. 在文件管理器里右键 `samples/headers-normal.eml`，看“使用其他应用打开”的列表。
- **预期**：
  - 包不再是已安装状态；清单里的文件都不存在；`.desktop` 文件和命令都不在了。
  - `mimeinfo.cache` 里没有本应用；`gio mime` 的 `Registered applications` 里没有本应用；“使用其他应用打开”列表里没有它。
  - `xdg-mime query default` 不再返回本应用的 `.desktop` 文件名。**例外**：如果之前手动执行过 `xdg-mime default ...`（比如为了做 OPEN-07），`~/.config/mimeapps.list` 里会留着 `message/rfc822=...desktop` 这一行，它属于用户配置，不是卸载的责任；此时以 `mimeinfo.cache` 和 `gio mime` 为准，在备注里写明。
  - 这些列表如果过一会儿才更新，或者要手动执行 `sudo update-desktop-database` 才更新，也要在备注里写明（说明卸载时没有触发刷新）。
  - 用户数据（`~/.local/share/com.github.realzony.emailfileviewer/`）保留是正常的。

## 6. 问题登记表

任何 `❌`、拿不准的现象、以及 1.4 里要求登记的环境问题，都在这里登记一行。编号用 `ISS-01`、`ISS-02`……往下编。

- “现象”一栏的开头写关联的检查项编号，例如 `OPEN-10：……`。
- “平台”写总表里的列名，同一个问题在多个平台出现时，合并成一行并写全。
- “截图”写截图文件名（建议放在 `docs/verification/screenshots/` 下，文件名形如 `ISS-01-win10.png`；目录自行创建），或者写“无”。

| 编号 | 平台 | 现象 | 复现步骤 | 截图 |
|------|------|------|----------|------|
| ISS-01 | | | | |
| ISS-02 | | | | |
| ISS-03 | | | | |
| ISS-04 | | | | |
| ISS-05 | | | | |
| ISS-06 | | | | |
| ISS-07 | | | | |
| ISS-08 | | | | |
| ISS-09 | | | | |
| ISS-10 | | | | |

## 7. 验收结论与回填

### 7.1 每个平台的结论

| 平台 | ✅ 数 | ❌ 数 | N/A 数 | 是否可以通过验收 | 签名与日期 |
|------|------|------|--------|------------------|------------|
| Windows 10 1809 | | | | | |
| Windows 11 | | | | | |
| macOS 15 Apple Silicon | | | | | |
| macOS 15 Intel | | | | | |
| Ubuntu 22.04 | | | | | |
| Debian 13 | | | | | |
| Fedora 43 | | | | | |

通过标准（tasks.md 10.1）：每个场景都有通过记录，或者对应的失败已经登记在第 6 章。

### 7.2 验收完成后要回填的地方

- `openspec/changes/add-desktop-viewer/tasks.md`：
  - 9.6：UNINST-01、UNINST-02 全部通过后勾选。
  - 10.1：第 3 章全部有结果（通过或已登记）后勾选。
  - 10.2：第 4 章全部有结果后勾选。
- `openspec/changes/add-desktop-viewer/design.md` 的风险部分，补上 Windows 和 Linux 的实验结果（任务 5.7 的要求）：
  - 新窗口拦截和链接：LINK-01、SEC-09、SEC-10 在 WebView2 和 WebKitGTK 上的结果；
  - 页内锚点：LINK-03；相对链接和空链接：SEC-11；
  - 脚本与应用接口隔离：SEC-01 到 SEC-03 在两个平台上的行为（注明只能观察行为）；
  - sandbox、srcdoc、CSP 在 WebView2 和 WebKitGTK 上的差异：VIEW-04、SEC-04、SEC-05、SEC-08 的结果。
  - 任何一个平台拦截无效，就按设计实现退路。
- `docs/release.md`：1.4 里登记的环境问题（FUSE、缺少的 C++ 运行库等）需要写进发布说明或前置要求。

## 附录 A：规格场景对照

规格里的 58 个场景都对应到下面的检查项。括号里是补充覆盖的检查项。

**email-opening**

| 需求 / 场景 | 检查项（括号内为补充覆盖） | 覆盖方式 |
|------|------|------|
| 通过文件对话框打开 / 选择单个文件 | OPEN-01（OPEN-03） | 手动 |
| 通过文件对话框打开 / 取消对话框 | OPEN-02 | 手动 |
| 通过拖放打开 / 拖入一个 EML 文件 | OPEN-04（OPEN-05） | 手动 |
| 通过拖放打开 / 拖入非文件内容 | OPEN-06 | 手动 |
| 通过系统文件关联打开 / 双击文件启动应用 | OPEN-07（OPEN-09） | 手动 |
| 通过系统文件关联打开 / 命令行传入路径 | OPEN-08 | 手动 |
| 单实例转发 / 应用运行中再双击另一个文件 | OPEN-10（OPEN-11） | 手动 |
| 按内容识别文件格式 / 扩展名与内容不符 | OPEN-12 | 手动 |
| 无法识别或损坏的文件 / 打开一张图片 | OPEN-13（OPEN-16） | 手动 |
| 无法识别或损坏的文件 / 解析过程中崩溃 | OPEN-14 | 手动（外围行为）+ 自动化（见附录 B） |
| 一次打开多个文件 / 多个文件中有一个损坏 | OPEN-15（OPEN-05） | 手动 |

**email-viewing**

| 需求 / 场景 | 检查项（括号内为补充覆盖） | 覆盖方式 |
|------|------|------|
| 显示邮件头 / 普通邮件 | VIEW-01（VIEW-02） | 手动 |
| 显示邮件头 / 缺少某个邮件头 | VIEW-02 | 手动 |
| 日期按本地时区显示 / 跨时区邮件 | VIEW-03 | 手动 + 自动化 |
| 正文格式选择 / 同时有 HTML 和纯文本 | VIEW-04 | 手动 |
| 正文格式选择 / 只有 RTF 正文的 MSG | VIEW-05（VIEW-06、VIEW-10） | 手动 |
| 字符集解码 / GBK 编码的中文邮件 | VIEW-07 | 手动 |
| 字符集解码 / 未声明字符集 | VIEW-08（VIEW-09） | 手动 |
| 内嵌图片 / 带签名图片的邮件 | VIEW-11 | 手动 |
| 正文在沙箱中渲染 / 正文包含脚本 | SEC-01（SEC-03） | 手动（只能看行为） |
| 正文在沙箱中渲染 / 正文尝试调用应用接口 | SEC-02 | 手动（只能看行为）+ 自动化 |
| 远程内容默认不加载 / 营销邮件中的远程图片 | SEC-04 | 手动 |
| 远程内容默认不加载 / 用户选择加载 | SEC-05（SEC-08） | 手动 |
| 查看时不产生网络请求 / 离线打开邮件 | SEC-06（SEC-07） | 手动 |
| 正文链接在系统浏览器中打开 / 点击正文链接 | LINK-01（LINK-03） | 手动 |
| 正文链接在系统浏览器中打开 / 链接文字与实际地址不符 | LINK-02 | 手动 |
| 加密与签名邮件提示 / 加密邮件 | SMIME-01 | 手动 |
| 加密与签名邮件提示 / 签名邮件 | SMIME-02 | 手动 |

**email-attachments**

| 需求 / 场景 | 检查项（括号内为补充覆盖） | 覆盖方式 |
|------|------|------|
| 列出附件 / 带两个附件的邮件 | ATT-01 | 手动 |
| 列出附件 / 内嵌图片不重复列出 | ATT-02 | 手动 |
| 保存单个附件 / 保存 PDF 附件 | ATT-03 | 手动 |
| 保存全部附件 / 目标文件夹已有同名文件 | ATT-04 | 手动 |
| 附件文件名的安全处理 / 文件名包含路径穿越 | ATT-05 | 手动 |
| 用系统默认程序打开附件 / 打开图片附件 | ATT-06 | 手动 |
| 用系统默认程序打开附件 / 打开可执行附件 | ATT-08 | 手动 |
| 打开嵌套邮件 / MSG 中附带另一封 MSG | ATT-09（ATT-11） | 手动 |
| 打开嵌套邮件 / 多层嵌套 | ATT-10 | 手动 |
| 展开 winmail.dat / Outlook 发出的 winmail.dat | ATT-12 | 手动 |

**app-localization**

| 需求 / 场景 | 检查项（括号内为补充覆盖） | 覆盖方式 |
|------|------|------|
| 默认跟随系统语言 / 系统语言为简体中文 | LOC-01 | 手动 + 自动化 |
| 默认跟随系统语言 / 系统语言为德语 | LOC-02 | 手动 + 自动化 |
| 手动切换语言 / 切换为英文并重启 | LOC-03 | 手动 + 自动化 |
| 界面语言不改变邮件内容 / 中文界面查看英文邮件 | LOC-04 | 手动 + 自动化 |

**app-appearance**

| 需求 / 场景 | 检查项（括号内为补充覆盖） | 覆盖方式 |
|------|------|------|
| 明暗模式 / 首次启动跟随系统 | APPR-01 | 手动 + 自动化 |
| 明暗模式 / 运行中系统切换外观 | APPR-02 | 手动 + 自动化 |
| 明暗模式 / 手动固定为浅色 | APPR-03 | 手动 + 自动化 |
| 预设配色方案 / 切换配色方案 | APPR-04 | 手动 + 自动化 |
| 预设配色方案 / 配色方案与明暗模式组合 | APPR-05 | 手动 + 自动化 |
| 外观设置持久保存 / 重启后保持设置 | APPR-06 | 手动 + 自动化 |
| 可读性 / 检查全部配色 | APPR-07 | 由自动化测试覆盖 |
| 外观不改变邮件内容 / 深色模式下查看 HTML 邮件 | APPR-08 | 手动 + 自动化 |
| 外观不改变邮件内容 / 深色模式下查看纯文本邮件 | APPR-09 | 手动 + 自动化 |

**app-distribution**

| 需求 / 场景 | 检查项（括号内为补充覆盖） | 覆盖方式 |
|------|------|------|
| 支持的平台 / 在各最低版本上运行 | DIST-01 | 手动 |
| 无需额外安装运行时 / 全新的 Windows 10 | DIST-02（DIST-03、DIST-04） | 手动 |
| 安装包形态 / 查看发布页 | DIST-05（PORT-08） | 手动 |
| Windows 绿色版免安装运行 / 解压即用 | PORT-01 | 手动 |
| Windows 绿色版免安装运行 / 不留下注册表和文件关联 | PORT-02 | 手动 |
| Windows 绿色版的数据位置 / 复制到另一台电脑 | PORT-03（PORT-04） | 手动 |
| Windows 绿色版的数据位置 / 程序目录不可写 | PORT-05 | 手动 |
| 缺少 WebView2 时的提示 / 在没有 WebView2 的系统上运行绿色版 | PORT-06 | 手动 |
| macOS 签名与公证 / 首次打开下载的应用 | DIST-07 | 手动（9.4 完成后） |
| Windows 代码签名 / 运行安装程序 | DIST-08 | 手动（9.5 完成后） |
| 卸载 / Windows 上卸载 | UNINST-01（UNINST-02） | 手动 |
| 发布产物可追溯 / 校验下载的安装包 | DIST-06（DIST-05） | 手动 |

## 附录 B：由自动化测试覆盖的场景

下面这些场景（或场景的一部分）在桌面上无法稳定地手动复现，由自动化测试覆盖。表中的测试文件和测试名都在仓库里，可以直接运行验证。

| 场景 | 无法手动验证的部分 | 自动化测试 |
|------|--------------------|------------|
| email-opening / 无法识别或损坏的文件 / 解析过程中崩溃 | 让解析进程恰好在解析某个文件时异常终止（没有样本能让 sidecar 崩溃） | `core/tests/EmailFileViewer.Core.Tests/FileFormatDetectionTests.cs`（两条 Trait 为“解析过程中崩溃”的测试）；`ui/src/__tests__/viewer-open.test.tsx`（“场景‘解析过程中崩溃’：显示已损坏或无法解析的提示，界面不退出，随后打开其他文件仍然成功”）；`src-tauri/src/sidecar/tests.rs`（`a_crash_fails_every_request_in_flight_at_once`、`the_next_request_restarts_the_sidecar_and_handshakes_again`、`the_file_that_crashed_the_sidecar_is_not_retried`、`a_malformed_file_that_crashes_the_sidecar_does_not_take_other_tabs_down`）。手动部分见 OPEN-14 |
| app-appearance / 可读性 / 检查全部配色 | 逐色计算对比度 | `ui/scripts/check-contrast.ts`（`cd ui && npm run contrast`，CI 的 `.github/workflows/ui.yml` 运行）；`ui/src/theme/__tests__/theme.test.ts` |
| 请求超时（不是规格场景，是 sidecar 监管的行为） | 让一个请求超时 | `src-tauri/src/sidecar/tests.rs`（`a_request_that_never_gets_a_reply_times_out_and_leaves_the_pending_table`、`each_method_uses_its_own_timeout`、`a_parse_that_times_out_restarts_the_sidecar_and_open_documents_survive`） |
| email-viewing / 正文在沙箱中渲染（sandbox 属性、CSP 字符串、capabilities 的作用范围） | 在发布版里直接检查 iframe 的属性和 IPC 可达性 | `ui/src/viewer/__tests__/bodyDocument.test.ts`；`core/tests/EmailFileViewer.Core.Tests/HtmlSanitizerTests.cs`、`HtmlProcessingSampleTests.cs`；`src-tauri/capabilities/default.json`。手动部分见 SEC-01 到 SEC-03 |

另外，下列场景手动验证了真实系统的表现，同时有自动化测试守着逻辑，列在这里供对照：

- 日期按本地时区（VIEW-03）：`ui/src/__tests__/viewer-mail.test.tsx`、`core/tests/EmailFileViewer.Core.Tests/EmlHeaderTests.cs`。
- app-localization 的四个场景（LOC-01 到 LOC-04）和 app-appearance 里除可读性以外的八个场景（APPR-01 到 APPR-06、APPR-08、APPR-09）：`ui/src/__tests__/App.test.tsx`；系统语言映射另有 `ui/src/settings/__tests__/language.test.ts` 和 `src-tauri/src/settings.rs` 里的测试。
- email-viewing 的各个场景在 Core 一侧还有带 `Trait("Scenario", ...)` 标注的单元测试（`core/tests/EmailFileViewer.Core.Tests/`）。

## 附录 C：样本使用情况

每个样本至少被一条检查项引用。用这张表确认“用全部样本逐条核对”没有漏掉哪个文件。

| 样本 | 用到它的检查项 |
|------|------|
| `samples/EmailWithAttachments.msg` | ATT-02、VIEW-11、VIEW-12 |
| `samples/EmailWithInnerMailAndAttachments.msg` | ATT-09 |
| `samples/EmailWithMsgAttachment.msg` | ATT-09、OPEN-14 |
| `samples/EmailWithMultilineSubject.eml` | VIEW-12 |
| `samples/HtmlSampleEmailWithAttachment.msg` | APPR-08、VIEW-12 |
| `samples/RtfSampleEmail.msg` | VIEW-05 |
| `samples/RtfWithShortRussianString.msg` | TITLE-01、VIEW-12 |
| `samples/TestWithAttachmentsAndInlines.eml` | ATT-11、VIEW-11 |
| `samples/TxtSampleEmail.msg` | APPR-09、VIEW-06、VIEW-12 |
| `samples/attachment-eml-file.eml` | ATT-11 |
| `samples/attachment-msg-file.eml` | ATT-11 |
| `samples/attachments-duplicate-names.eml` | ATT-04 |
| `samples/attachments-executables.eml` | ATT-08 |
| `samples/attachments-pdf-xlsx-jpg.eml` | ATT-01、ATT-03、ATT-06、ATT-07、LOC-04 |
| `samples/attachments-unsafe-names.eml` | ATT-05 |
| `samples/body-html-and-plain.eml` | APPR-08、KEY-03、KEY-09、LINK-01、LOC-04、OPEN-05、OPEN-10、TAB-01、VIEW-04 |
| `samples/body-html-in-rtf.msg` | VIEW-05 |
| `samples/body-multipart-mixed-inline-images.eml` | VIEW-10 |
| `samples/body-plain-only.eml` | APPR-05、APPR-09、KEY-01、KEY-02、KEY-05、KEY-08、OPEN-03、OPEN-05、OPEN-08、OPEN-11、OPEN-14、OPEN-15、SEC-02、TAB-01、VIEW-06 |
| `samples/body-rtf-only.msg` | DIST-01、DIST-02、OPEN-01、OPEN-02、OPEN-03、OPEN-07、OPEN-09、UNINST-01、UNINST-02、VIEW-05 |
| `samples/charset-big5-undeclared.eml` | OPEN-05、VIEW-08 |
| `samples/charset-gb18030.eml` | VIEW-09 |
| `samples/charset-gbk-raw-header.eml` | VIEW-09 |
| `samples/charset-gbk.eml` | LOC-04、OPEN-05、VIEW-07 |
| `samples/charset-iso-2022-jp-thunderbird.eml` | VIEW-09 |
| `samples/charset-iso-2022-jp.eml` | VIEW-09 |
| `samples/charset-koi8-r-raw-header.eml` | VIEW-09 |
| `samples/charset-shift-jis.eml` | VIEW-09 |
| `samples/cid-inline-image.eml` | ATT-02、VIEW-11 |
| `samples/format-eml-disguised-as-msg.msg` | OPEN-12 |
| `samples/format-msg-disguised-as-eml.eml` | OPEN-12 |
| `samples/headers-normal.eml` | APPR-01、APPR-02、APPR-03、APPR-04、APPR-06、DIST-01、DIST-02、DIST-03、DIST-04、KEY-01、KEY-02、KEY-03、KEY-07、KEY-08、KEY-09、LOC-01、LOC-02、LOC-03、OPEN-03、OPEN-04、OPEN-05、OPEN-06、OPEN-08、OPEN-09、OPEN-10、OPEN-13、OPEN-14、OPEN-15、OPEN-16、SEC-02、TAB-01、TITLE-01、UNINST-01、UNINST-02、VIEW-01 |
| `samples/headers-rfc2047.eml` | VIEW-09 |
| `samples/headers-timezone-bcc-replyto-no-cc.eml` | VIEW-02、VIEW-03 |
| `samples/html-malicious.eml` | SEC-01、SEC-02、SEC-03、SEC-05、SEC-07、SEC-08 |
| `samples/link-text-mismatch.eml` | LINK-01、LINK-02、SEC-07 |
| `samples/links-anchors-and-schemes.eml` | LINK-03、LINK-04、SEC-07、SEC-09、SEC-10、SEC-11 |
| `samples/marketing-remote-content.eml` | APPR-05、APPR-08、SEC-04、SEC-05、SEC-07、SEC-08 |
| `samples/nested-msg-two-levels.msg` | ATT-10 |
| `samples/nested-rfc822-two-levels.eml` | ATT-10、TITLE-01 |
| `samples/simple-embedded-message.eml` | ATT-11 |
| `samples/simple-multipart.eml` | VIEW-12 |
| `samples/smime-encrypted.eml` | SMIME-01 |
| `samples/smime-msg-encrypted.msg` | SMIME-01 |
| `samples/smime-msg-signed-clear.msg` | SMIME-02 |
| `samples/smime-msg-signed-opaque.msg` | SMIME-02 |
| `samples/smime-signed-opaque.eml` | SMIME-02 |
| `samples/smime-signed-thunderbird.eml` | SEC-07、SMIME-02 |
| `samples/smime-signed.eml` | SMIME-02 |
| `samples/tnef-winmail-docx.eml` | ATT-12 |
| `samples/invalid/empty.eml` | OPEN-13 |
| `samples/invalid/not-an-email.png` | OPEN-13、TITLE-01 |
| `samples/invalid/png-disguised-as-eml.eml` | OPEN-13 |
| `samples/invalid/truncated-half.eml` | OPEN-14 |
| `samples/invalid/truncated.msg` | OPEN-14、OPEN-15 |
| `samples/assets/*` | 营销邮件的远程素材（图片、追踪像素、样式表），不单独检查；其中 `photo.jpg` 用于 ATT-03 的字节对照 |

手工创建的文件（不在仓库里）：`samples/efv-unreadable.eml`（OPEN-16）。
