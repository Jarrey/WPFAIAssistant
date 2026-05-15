using CommunityToolkit.Mvvm.ComponentModel;

namespace WPFAIAssistant.ViewModels
{
    public partial class SkillViewModel : ObservableObject
    {
        public string Name { get; }
        public string FilePath { get; }
        public string Source { get; }

        [ObservableProperty] private bool _isEnabled;

        public SkillViewModel(string name, string filePath, bool isEnabled, string source)
        {
            Name = name;
            FilePath = filePath;
            _isEnabled = isEnabled;
            Source = source;
        }
    }
}
