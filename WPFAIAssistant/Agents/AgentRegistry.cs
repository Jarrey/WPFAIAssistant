using Microsoft.Extensions.AI;

namespace WPFAIAssistant.Agents
{
    /// <summary>
    /// Collects all registered agents and exposes their AIFunctions.
    /// Inject IAgentRegistry wherever you need to add or enumerate agents.
    /// </summary>
    public interface IAgentRegistry
    {
        IReadOnlyList<IAgent> Agents { get; }
        void Register(IAgent agent);

        /// <summary>Returns all AIFunction instances from all registered agents.</summary>
        IReadOnlyList<AIFunction> GetAIFunctions();
    }

    public class AgentRegistry : IAgentRegistry
    {
        private readonly List<IAgent> _agents = new();

        public IReadOnlyList<IAgent> Agents => _agents.AsReadOnly();

        public void Register(IAgent agent) => _agents.Add(agent);

        public IReadOnlyList<AIFunction> GetAIFunctions()
        {
            var functions = new List<AIFunction>();
            foreach (var agent in _agents)
                functions.AddRange(agent.GetAIFunctions());
            return functions;
        }
    }
}
