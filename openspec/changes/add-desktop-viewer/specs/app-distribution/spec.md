# Spec Delta

## Purpose

定义应用在 Windows、macOS、Linux 上的分发方式：支持的最低平台、安装包形态、签名与公证要求，以及发布产物的来源与可校验性。

## ADDED Requirements

### Requirement: 支持的平台
系统 SHALL 至少支持以下平台：Windows 10（1809 及以上）和 Windows 11 的 x64；macOS 15 及以上的 Apple Silicon 和 Intel；Linux x64 上的 Ubuntu 22.04+、Debian 13+、Fedora 43+。更旧的系统版本可能可以运行，但不作保证。

#### Scenario: 在各最低版本上运行
- **WHEN** 在上述每个平台的最低版本上安装并启动应用，打开一封示例邮件
- **THEN** 应用正常启动并正确显示该邮件

### Requirement: 无需额外安装运行时
用户 SHALL NOT 需要单独安装 .NET 或其他运行时。Windows 上缺少 WebView2 运行时时，安装程序 SHALL 自动安装；Linux 的 deb 包 SHALL 声明依赖，由包管理器自动安装。

#### Scenario: 全新的 Windows 10
- **WHEN** 在没有安装 .NET、也没有 WebView2 运行时的 Windows 10 上运行安装程序
- **THEN** 安装完成后应用可以正常启动和打开邮件

### Requirement: 安装包形态
每次发布 SHALL 提供：Windows 安装程序（`.exe`）、macOS 磁盘映像（`.dmg`，分别提供 Apple Silicon 和 Intel 版本，或提供一个通用版本）、Linux 的 AppImage 和 `.deb`。

#### Scenario: 查看发布页
- **WHEN** 用户打开某个版本的发布页
- **THEN** 能找到上述每个平台对应的安装包

### Requirement: macOS 签名与公证
macOS 安装包中的所有可执行文件 SHALL 使用 Developer ID 签名，整个应用 SHALL 通过 Apple 公证，使用户首次打开时不会看到"无法验证开发者"的拦截提示。

#### Scenario: 首次打开下载的应用
- **WHEN** 用户从浏览器下载 dmg、把应用拖入"应用程序"并首次打开
- **THEN** 系统不显示"无法验证开发者"或"已损坏"的拦截提示

### Requirement: Windows 代码签名
Windows 安装程序和应用中的所有可执行文件 SHALL 经过代码签名，用户在安装和运行时 SHALL 能看到发布者名称，而不是"未知发布者"。

#### Scenario: 运行安装程序
- **WHEN** 用户运行下载的 Windows 安装程序
- **THEN** 系统的权限确认对话框显示发布者名称，而不是"未知发布者"

### Requirement: 卸载
在 Windows 和通过 deb 安装的 Linux 上，卸载应用 SHALL 移除应用文件及其注册的文件关联。

#### Scenario: Windows 上卸载
- **WHEN** 用户通过系统设置卸载应用
- **THEN** 应用文件被删除，`.eml` / `.msg` 不再以本应用作为打开方式

### Requirement: 发布产物可追溯
所有发布的安装包 SHALL 由持续集成从带版本标签的提交自动构建，发布页 SHALL 同时提供每个安装包的 SHA-256 校验值。

#### Scenario: 校验下载的安装包
- **WHEN** 用户计算下载的安装包的 SHA-256 值
- **THEN** 结果与发布页上公布的校验值一致
