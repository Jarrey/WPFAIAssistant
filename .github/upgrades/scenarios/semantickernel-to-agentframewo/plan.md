# Semantic Kernel to Agent Framework Plan

## Overview

**Target**: Validate and execute Semantic Kernel Agents to Agent Framework migration.
**Scope**: Single WPF project (`WPFAIAssistant`) with Semantic Kernel core usage but no SK Agents package/API usage.

## Tasks

### 01-scope-validation-and-outcome

Validate whether the selected migration scenario applies to the current codebase, document findings, and finalize the outcome.

**Done when**: Evidence is documented for package/API usage, the migration applicability decision is recorded, and the workflow progress is updated.

---

### 02-refactor-sk-core-tool-calling

Refactor the current Semantic Kernel core tool-calling implementation to a native OpenAI-compatible tool invocation pipeline, while preserving existing user-facing chat behavior and local tool functionality.

**Done when**: Semantic Kernel core dependencies are removed from runtime code and project packages, tool invocation still works for non-reasoning models, and the solution builds successfully.

---
