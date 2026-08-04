---
kind: external_dependency
name: Tesseract OCR 文字识别库
slug: tesseractocr
category: external_dependency
category_hints:
    - vendor_identity
    - framework_behavior
scope:
    - '**'
source_files:
    - ShaoLu/ShaoLu.csproj
    - ShaoLu/tessdata/README.txt
---

### TesseractOCR
- 角色：作为 PaddleOCRSharp 的补充 OCR 引擎，提供传统 OCR 能力。
- 语言数据：项目自带 tessdata 目录，包含中文简体/繁体、英文、日文及竖排语言的 `.traineddata` 文件。
- 用途：配合 ScreenTextReader 实现屏幕文字读取功能。