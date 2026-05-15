using Microsoft.SemanticKernel;

namespace WPFAIAssistant.Agents
{
    /// <summary>
    /// Collects all registered agents and exposes them to the Kernel.
    /// Inject IAgentRegistry wherever you need to add or enumerate agents.
    /// </summary>
    public interface IAgentRegistry
    {
        IReadOnlyList<IAgent> Agents { get; }
        void Register(IAgent agent);

        /// <summary>Imports every registered agent as a SK plugin into the given kernel.</summary>
        void ApplyToKernel(Kernel kernel);
    }

    public class AgentRegistry : IAgentRegistry
    {
        private readonly List<IAgent> _agents = new();

        public IReadOnlyList<IAgent> Agents => _agents.AsReadOnly();

        public void Register(IAgent agent) => _agents.Add(agent);

        public void ApplyToKernel(Kernel kernel)
        {
            foreach (var agent in _agents)
                agent.Register(kernel);
        }
    }
}
