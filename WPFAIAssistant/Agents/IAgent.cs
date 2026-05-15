using System.Text.Json;

namespace WPFAIAssistant.Agents
{
    /// <summary>
    /// Base interface for all AI agent tools.
    /// Implement this to create a new tool available to the AI.
    /// </summary>
    public interface IAgent
    {
        string PluginName { get; }
        string Description { get; }

        /// <summary>Returns tool definitions exposed by this agent.</summary>
        IReadOnlyList<AgentToolDefinition> GetToolDefinitions();

        /// <summary>Invoke a tool by name with JSON arguments.</summary>
        string Invoke(string toolName, JsonElement arguments);
    }

    public sealed class AgentToolDefinition
    {
        public required string Name { get; init; }
        public required string Description { get; init; }
        public required Dictionary<string, object> ParametersSchema { get; init; }
    }
}
