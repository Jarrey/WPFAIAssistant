using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Windows;
using WPFAIAssistant.Agents;
using WPFAIAssistant.Services;
using WPFAIAssistant.ViewModels;

namespace WPFAIAssistant
{
    public partial class App : Application
    {
        public static IServiceProvider Services { get; private set; } = null!;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                .Build();

            var services = new ServiceCollection();
            services.AddSingleton<IConfiguration>(configuration);

            // Agent registry
            var registry = new AgentRegistry();
            registry.Register(new FileSystemAgent());
            services.AddSingleton<IAgentRegistry>(registry);

            // Core services
            services.AddSingleton<IAIService, DeepSeekAIService>();
            services.AddSingleton<ISessionService, SessionService>();
            services.AddSingleton<ISkillService, SkillService>();

            // View models and view
            services.AddSingleton<MainWindowViewModel>();
            services.AddSingleton<MainWindow>();

            Services = services.BuildServiceProvider();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
    }
}

