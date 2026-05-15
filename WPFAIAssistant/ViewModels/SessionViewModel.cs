using CommunityToolkit.Mvvm.ComponentModel;

namespace WPFAIAssistant.ViewModels
{
    public partial class SessionViewModel : ObservableObject
    {
        public string Id { get; }

        [ObservableProperty] private string _title;
        [ObservableProperty] private DateTime _updatedAt;
        [ObservableProperty] private bool _isActive;

        public string UpdatedAtDisplay => UpdatedAt.ToString("MM-dd HH:mm");

        public SessionViewModel(string id, string title, DateTime updatedAt)
        {
            Id = id;
            _title = title;
            _updatedAt = updatedAt;
        }

        partial void OnUpdatedAtChanged(DateTime value)
        {
            OnPropertyChanged(nameof(UpdatedAtDisplay));
        }
    }
}
