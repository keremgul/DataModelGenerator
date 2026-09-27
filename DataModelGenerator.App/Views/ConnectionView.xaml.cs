using System.ComponentModel;
using System.Windows.Controls;
using DataModelGenerator.App.ViewModels;

namespace DataModelGenerator.App.Views;

public partial class ConnectionView : UserControl
{
    private ConnectionViewModel? _viewModel;

    public ConnectionView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as ConnectionViewModel;

        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        SyncPasswordBox();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConnectionViewModel.ApiKey))
            SyncPasswordBox();
    }

    /// <summary>PasswordBox.Password bağlanabilir bir DependencyProperty değildir; el ile eşitlenir.</summary>
    private void KeyBox_PasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (_viewModel is not null && _viewModel.ApiKey != KeyBox.Password)
            _viewModel.ApiKey = KeyBox.Password;
    }

    private void SyncPasswordBox()
    {
        if (_viewModel is not null && KeyBox.Password != _viewModel.ApiKey)
            KeyBox.Password = _viewModel.ApiKey;
    }
}
