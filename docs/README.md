# 文档索引

## 当前维护

- [交付说明](handoff.md)：安装、升级、卸载与日常操作。
- [统一设置页面](unified-settings.md)：通用偏好、三个工具的折叠分类与验收范围。
  [验证记录](validation/unified-settings.md)包含本次回归、视觉检查与审查修复结果。
- [正式规格](spec.md)：产品行为、实现决定与范围边界。
- [术语表](../GLOSSARY.md)：项目共享术语。
- [模块接入约定](module-integration.md)：宿主与模块的接口、生命周期和隔离要求。
- [视觉规范](design-system.md)：实现中的主题与组件约定，参考稿为 [Apple Light](UI%20Design/Apple%20Light.md) 和 [Apple Dark](UI%20Design/Apple%20Dark.md)。
- [工单索引](tickets/README.md)：位于 `docs/tickets/`，保留已完成工单的实施结果、依赖与检查入口。
- [最终回归记录](validation/ticket-09.md)与[审查修复记录](validation/code-review-fixes.md)：自动检查证据与待人工复验的场景。

## 决策与来源

- [ADR-0001](adr/0001-retain-existing-data-stores.md)：保留现有数据存储。
- [ADR-0002](adr/0002-single-elevated-wpf-host.md)：一个管理员权限 WPF 宿主。
- [ADR-0003](adr/0003-independent-fresh-data.md)：套件独立数据并从空白开始。
- [原项目来源](source-provenance.md)：复制时的版本与工作区状态。

## 历史归档

2026-10-21 的规划材料已归档；其中的待实施状态与计划描述反映当时情况。
当前实现状态以工单顶部的实施结果和验证记录为准。

- [讨论记录](archive/discussion.md)
- [方案摘要](archive/plan.md)
- [工单拆分记录](archive/ticket-breakdown.md)

原 `spec-draft.md` 仅是正式规格的跳转页，整理时已删除，直接使用[正式规格](spec.md)。
