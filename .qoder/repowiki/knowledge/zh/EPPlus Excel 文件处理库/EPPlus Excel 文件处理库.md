---
kind: external_dependency
name: EPPlus Excel 文件处理库
slug: epplus
category: external_dependency
category_hints:
    - vendor_identity
    - sdk_real_api
scope:
    - '**'
source_files:
    - ShaoLu/ShaoLu.csproj
---

### EPPlus
- 角色：Excel 文件读写库，用于 TypeTextFromFile 步骤从 .xlsx 文件中读取数据。
- 用途：支持从 Excel 工作表的第一列逐行读取数据，配合文本输入步骤实现批量数据输入。