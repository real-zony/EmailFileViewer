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
每次发布 SHALL 提供：Windows 安装程序（`.exe`）和 Windows 绿色版（`.zip`）、macOS 磁盘映像（`.dmg`，分别提供 Apple Silicon 和 Intel 版本，或提供一个通用版本）、Linux 的 AppImage 和 `.deb`。

#### Scenario: 查看发布页
- **WHEN** 用户打开某个版本的发布页
- **THEN** 能找到上述每个平台对应的安装包

### Requirement: Windows 绿色版免安装运行
Windows 绿色版 SHALL 解压后即可运行，不需要安装，也不需要管理员权限；它 SHALL NOT 写入注册表、SHALL NOT 注册文件关联。

#### Scenario: 解压即用
- **WHEN** 用户在已有 WebView2 运行时的 Windows 10 或 11 上解压绿色版，运行其中的程序
- **THEN** 应用正常启动，能打开、查看邮件并保存附件

#### Scenario: 不留下注册表和文件关联
- **WHEN** 用户运行绿色版并打开过几封邮件
- **THEN** 系统中没有新增本应用的注册表项和 `.eml` / `.msg` 文件关联

### Requirement: Windows 绿色版的数据位置
绿色版 SHALL 把设置和 WebView 数据保存在程序所在目录下的 `data` 文件夹中，复制整个文件夹到另一台电脑后设置 SHALL 保留，删除整个文件夹 SHALL 清除全部持久数据。程序所在目录不可写时，SHALL 改用当前用户的应用数据目录，并照常运行。打开附件时产生的临时文件仍放在系统临时目录，并按现有规则在退出时清理。

#### Scenario: 复制到另一台电脑
- **WHEN** 用户在绿色版中切换为深色模式和简体中文，然后把整个文件夹复制到另一台电脑上运行
- **THEN** 应用以深色模式和简体中文启动

#### Scenario: 程序目录不可写
- **WHEN** 绿色版被放在当前用户没有写权限的目录中运行
- **THEN** 应用正常启动，设置保存到当前用户的应用数据目录

### Requirement: 缺少 WebView2 时的提示
系统中没有 WebView2 运行时时，应用 SHALL 显示一个系统对话框，说明需要先安装 WebView2 运行时并给出微软官方下载地址，然后退出；SHALL NOT 无提示地闪退。

#### Scenario: 在没有 WebView2 的系统上运行绿色版
- **WHEN** 用户在没有安装 WebView2 运行时的 Windows 10 上运行绿色版
- **THEN** 系统对话框说明缺少 WebView2 并给出下载地址，用户确认后应用退出

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
