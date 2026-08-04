---
kind: external_dependency
name: PaddleOCRSharp OCR 识别引擎
slug: paddleocrsharp
category: external_dependency
category_hints:
    - vendor_identity
    - sdk_real_api
    - client_constraint
scope:
    - '**'
source_files:
    - ShaoLu/ShaoLu.csproj
    - ShaoLu/Services/OCRService.cs
---

### PaddleOCRSharp
- 角色：项目中的 OCR 文字识别引擎，通过 PaddleOCRSharp NuGet 包集成，用于从屏幕截图或图片中识别文字。
- 集成方式：在 ShaoLu.csproj 中声明 `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` 以支持原生包的正确还原；项目默认平台已改为 x64 以匹配 PaddleOCRSharp 的 AMD64 架构。
- 注意：PaddleOCR.dll 只是 C++ 桥接层（约 903KB），真正的推理引擎在独立的运行时包中，缺少运行时会导致 `DllNotFoundException`。
- 验证：需确认具体 API 调用方式与官方文档一致。