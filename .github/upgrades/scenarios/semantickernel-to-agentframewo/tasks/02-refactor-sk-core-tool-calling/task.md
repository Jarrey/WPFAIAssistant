# 02-refactor-sk-core-tool-calling: Refactor SK Core Tool Calling

Refactor the current Semantic Kernel core tool-calling implementation to a native OpenAI-compatible tool invocation pipeline, while preserving existing user-facing chat behavior and local tool functionality.

**Done when**: Semantic Kernel core dependencies are removed from runtime code and project packages, tool invocation still works for non-reasoning models, and the solution builds successfully.

## Findings

- Existing runtime tool path was implemented via Semantic Kernel core (`Kernel.CreateBuilder`, `IChatCompletionService`, `[KernelFunction]`, `ImportPluginFromObject`).
- Tool surfaces already had clear method boundaries (`list_directory`, `get_file_info`, `get_directory_info`, `write_text_file`, `append_text_file`) suitable for direct schema mapping.
- Current chat flow already uses raw OpenAI-compatible HTTP/SSE for streaming, so only the pre-stream tool resolution pass required refactor.

## Execution Decisions

- Replaced SK plugin contract with a generic tool contract:
  - `IAgent.GetToolDefinitions()`
  - `IAgent.Invoke(toolName, JsonElement arguments)`
- Reworked `AgentRegistry` to aggregate tool schemas and dispatch tool invocations by tool name.
- Rewrote `DeepSeekAIService` non-reasoning pre-pass to:
  1. Call non-streaming `/chat/completions` with `tools` + `tool_choice=auto`
  2. Detect `tool_calls`
  3. Execute local tool handlers via registry
  4. Append `role=tool` messages
  5. Iterate for bounded turns, then keep final assistant response for streaming pass
- Preserved reasoning-model behavior (`deepseek-v4-pro` / `reasoner`) by skipping tool pass in thinking mode.

## Files Updated

- `WPFAIAssistant/Agents/IAgent.cs`
- `WPFAIAssistant/Agents/AgentRegistry.cs`
- `WPFAIAssistant/Agents/FileSystemAgent.cs`
- `WPFAIAssistant/Agents/FileOutputAgent.cs`
- `WPFAIAssistant/Services/DeepSeekAIService.cs`
- `WPFAIAssistant/WPFAIAssistant.csproj`
- `README.md`
