---
kind: external_dependency
name: NUnit 单元测试框架
slug: nunit
category: external_dependency
category_hints:
    - vendor_identity
    - framework_behavior
scope:
    - '**'
source_files:
    - NUnitTest/NUnitTest.csproj
---

### NUnit
- 角色：项目的单元测试框架，用于自动化测试步骤逻辑、条件评估器、转换器等功能。
- 已知问题：需要正确的绑定重定向才能发现测试用例，特别是 System.Numerics.Vectors 版本冲突时需要配置 app.config。
- 测试覆盖：包含 AutomationStepTests、ConditionEvaluatorTests、ConverterTests、ModelTests、StepExecutionContextTests、StepsFileTests 等测试类。