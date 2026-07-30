---
kind: external_dependency
name: Tesseract OCR 引擎
slug: tesseractocr
category: external_dependency
category_hints:
    - vendor_identity
scope:
    - '**'
---

### 身份与角色

### 集成要点
- 包标记为 `ExcludeAssets="compile;runtime"`，表示仅使用其托管接口，原生库通过 `tessdata\**` 内容文件复制到输出目录。
- 语言数据文件随应用一起分发，无需额外安装。

### 使用约束
- 与 PaddleOCRSharp 并存，可能用于不同识别场景或降级路径。
- 验证具体 API 调用方式请参考官方文档。