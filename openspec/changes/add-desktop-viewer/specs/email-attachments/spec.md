# Spec Delta

## Purpose

定义邮件附件的列出、保存、打开，以及嵌套邮件和 winmail.dat 的处理方式，并约束附件写入磁盘时的安全边界。

## ADDED Requirements

### Requirement: 列出附件
系统 SHALL 列出邮件的所有附件，每个附件显示文件名和大小；仅被正文以 `cid:` 引用、已显示在正文中的内嵌图片 SHALL NOT 出现在附件列表中。

#### Scenario: 带两个附件的邮件
- **WHEN** 打开一封带有一个 PDF 和一个 xlsx 附件的邮件
- **THEN** 附件列表显示这两个文件的文件名和大小

#### Scenario: 内嵌图片不重复列出
- **WHEN** 邮件正文通过 `cid:` 引用了一张图片，且另有一个 docx 附件
- **THEN** 附件列表只显示 docx

### Requirement: 保存单个附件
系统 SHALL 允许用户把单个附件保存到用户选择的位置，默认文件名为附件原名，保存的内容 SHALL 与附件原始字节完全一致。

#### Scenario: 保存 PDF 附件
- **WHEN** 用户对一个 PDF 附件选择"保存"并确认保存位置
- **THEN** 该位置生成一个与原附件字节完全一致的文件

### Requirement: 保存全部附件
系统 SHALL 允许用户把全部附件一次保存到所选文件夹；文件名冲突时 SHALL 自动加序号区分，SHALL NOT 覆盖已有文件。

#### Scenario: 目标文件夹已有同名文件
- **WHEN** 用户把附件 `report.pdf` 保存到一个已存在 `report.pdf` 的文件夹
- **THEN** 新文件以 `report (1).pdf` 之类的不冲突名称保存，原文件保持不变

### Requirement: 附件文件名的安全处理
写入磁盘时，系统 SHALL 清理附件文件名中的路径成分和各平台的非法字符，保证文件只会写入用户选择的文件夹内。

#### Scenario: 文件名包含路径穿越
- **WHEN** 附件文件名为 `../../.bashrc`
- **THEN** 文件被保存在用户选择的文件夹内，文件名中不含路径成分

### Requirement: 用系统默认程序打开附件
系统 SHALL 允许用户用系统默认程序打开附件；对于可执行文件和脚本类附件（如 `.exe`、`.bat`、`.ps1`、`.sh`、`.app`、`.command`），系统 SHALL NOT 直接打开，只允许保存。

#### Scenario: 打开图片附件
- **WHEN** 用户对一个 `.jpg` 附件选择"打开"
- **THEN** 该图片在系统默认的图片查看程序中打开

#### Scenario: 打开可执行附件
- **WHEN** 用户尝试打开一个 `.exe` 附件
- **THEN** 系统不执行该文件，提示出于安全原因只能保存

### Requirement: 打开嵌套邮件
当附件本身是一封邮件（MSG 中附带的 MSG、EML 中的 `message/rfc822` 部分、或作为附件的 `.eml` / `.msg` 文件）时，系统 SHALL 允许用户在应用内直接打开它，展示方式与打开普通邮件文件相同。

#### Scenario: MSG 中附带另一封 MSG
- **WHEN** 用户在一封 MSG 邮件的附件列表中打开一个嵌套的 MSG 附件
- **THEN** 系统在应用内显示这封嵌套邮件的邮件头、正文和附件

#### Scenario: 多层嵌套
- **WHEN** 嵌套邮件中还附带一封邮件
- **THEN** 用户可以继续在应用内打开下一层

### Requirement: 展开 winmail.dat
当附件为 TNEF 格式（通常名为 `winmail.dat`）时，系统 SHALL 提取其中包含的文件，并作为普通附件列出，而不是只显示 `winmail.dat` 本身。

#### Scenario: Outlook 发出的 winmail.dat
- **WHEN** 打开一封附件只有 `winmail.dat` 的邮件，其中封装了一个 docx 文件
- **THEN** 附件列表显示该 docx 文件，用户可以保存和打开它
