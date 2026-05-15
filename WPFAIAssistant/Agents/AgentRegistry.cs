using System.Text.Json;

namespace WPFAIAssistant.Agents
{
    /// <summary>
    /// Collects all registered agents and exposes their tool definitions.
    /// Inject IAgentRegistry wherever you need to add or enumerate agents.
    /// </summary>
    public interface IAgentRegistry
    {
        IReadOnlyList<IAgent> Agents { get; }
        void Register(IAgent agent);

        IReadOnlyList<AgentToolDefinition> GetToolDefinitions();
        string? TryInvoke(string toolName, JsonElement arguments);
    }

    public class AgentRegistry : IAgentRegistry
    {
        private readonly List<IAgent> _agents = new();

        public IReadOnlyList<IAgent> Agents => _agents.AsReadOnly();

        public void Register(IAgent agent) => _agents.Add(agent);

        public IReadOnlyList<AgentToolDefinition> GetToolDefinitions()
        {
            var tools = new List<AgentToolDefinition>();
            foreach (var agent in _agents)
                tools.AddRange(agent.GetToolDefinitions());
            return tools;
        }

        public string? TryInvoke(string toolName, JsonElement arguments)
        {
            foreach (var agent in _agents)
            {
                var defs = agent.GetToolDefinitions();
                if (defs.Any(d => string.Equals(d.Name, toolName, StringComparison.OrdinalIgnoreCase)))
                    return agent.Invoke(toolName, arguments);
            }

            return null;
        }
    }
}
