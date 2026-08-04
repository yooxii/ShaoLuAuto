---
kind: external_dependency
name: OpenCvSharp4 图像处理库
slug: opencvshar4
category: external_dependency
category_hints:
    - vendor_identity
    - client_constraint
scope:
    - '**'
source_files:
    - ShaoLu/ShaoLu.csproj
---

### OpenCvSharp4
- 角色：图像处理和计算机视觉功能的核心库，用于图片裁剪、编辑、特征提取等操作。
- 运行时：使用 slim 版本的 runtime 包，减少部署体积。
- 用途：ImageEdit 模块中的图片编辑、裁剪、点击点设置等功能都依赖此库进行图像处理。