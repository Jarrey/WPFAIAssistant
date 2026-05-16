# Semantic Kernel to Agent Framework

## Strategy
Scope-first validation, then execute only if SK Agents APIs are present.

## Preferences
- **Flow Mode**: Automatic
- **Commit Strategy**: After Each Task
- **Pace**: Standard
- **Source Branch**: main
- **Working Branch**: semantic-kernel-to-agent-framework-1
- **Pending Changes Handling**: Commit before branch switch

## Decisions
- Selected scenario is specifically SK Agents to Agent Framework migration.
- Repository currently uses Semantic Kernel core patterns, not SK Agents APIs.
- User approved proceeding with a custom refactor to remove Semantic Kernel core tool-calling and modernize the pipeline.
- User requested further modernization to Microsoft.Extensions.AI style where feasible.
