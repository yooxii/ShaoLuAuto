---
kind: external_dependency
name: OpenCvSharp4 图像处理库
slug: opencvsharp4
category: external_dependency
category_hints:
    - vendor_identity
scope:
    - '**'
---

### 身份与角色
项目通过 `OpenCvSharp4.Extensions` 和 `OpenCvSharp4.runtime.win.slim` 两个 NuGet 包集成 OpenCV 的 .NET 绑定，用于图片裁剪、编辑、模板匹配等图像处理功能。

### 集成要点
- Extensions 提供高级扩展方法，runtime.win.slim 提供精简版原生运行时。
- 主要用于图片编辑窗口中的裁剪、缩放、格式转换等操作。

### 使用约束
- 仅支持 Windows 平台。
- 验证具体 API 调用方式请参考官方文档。