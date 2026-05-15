using Microsoft.Extensions.AI;

namespace WPFAIAssistant.Agents
{
    /// <summary>
    /// Base interface for all AI agent tools.
    /// Implement this to expose AIFunctions to the chat pipeline.
    /// </summary>
    public interface IAgent
    {
        string PluginName { get; }
        string Description { get; }

        /// <summary>Returns Microsoft.Extensions.AI AIFunction instances for this agent.</summary>
        IReadOnlyList<AIFunction> GetAIFunctions();
    }
}
