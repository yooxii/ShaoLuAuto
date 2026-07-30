---
kind: external_dependency
name: NUnit 单元测试框架
slug: nunit
category: external_dependency
category_hints:
    - framework_behavior
scope:
    - '**'
---

### 身份与角色
项目使用 NUnit 作为单元测试框架，配合 NUnit3TestAdapter 在 Visual Studio 中运行测试。

### 集成要点
- 测试项目使用旧格式的 app.config 管理依赖绑定重定向。
- 测试适配器基于 Microsoft.Testing.Platform，需要正确的依赖解析。

### 使用约束
- 测试项目仍使用 packages.config 时代的遗留依赖管理方式。
- 需要在 app.config 中配置 bindingRedirects 解决版本冲突。
- 验证具体测试编写方式请参考 NUnit 官方文档。