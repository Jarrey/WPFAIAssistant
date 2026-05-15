using Microsoft.SemanticKernel;

namespace WPFAIAssistant.Agents
{
    /// <summary>
    /// Base interface for all AI agent plugins.
    /// Implement this to create a new tool available to the AI.
    /// </summary>
    public interface IAgent
    {
        string PluginName { get; }
        string Description { get; }

        /// <summary>Register this agent's SK functions into the kernel.</summary>
        void Register(Kernel kernel);
    }
}
