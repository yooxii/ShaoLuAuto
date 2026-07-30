---
kind: external_dependency
name: PaddleOCRSharp OCR 引擎
slug: paddleocrsharp
category: external_dependency
category_hints:
    - vendor_identity
    - sdk_real_api
scope:
    - '**'
---

### 身份与角色

### 集成要点
- 项目需在 `.csproj` 中声明 `<RuntimeIdentifiers>win-x64</RuntimeIdentifiers>` 以正确还原原生包。
- 默认平台必须设为 `x64`（而非 AnyCPU），否则会出现 MSB3270 架构不匹配警告。
- 输出目录会包含约 90MB 的 `paddle_inference.dll` 及多个大型依赖 DLL。

### 使用约束
- 仅支持 Windows x64 平台。
- 运行前需确保所有原生 DLL 已复制到输出目录。
- 验证具体 API 调用方式请参考官方文档。