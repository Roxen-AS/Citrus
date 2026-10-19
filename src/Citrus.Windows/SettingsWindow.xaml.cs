using Citrus.Core;

namespace Citrus.Windows;

public partial class SettingsWindow : Window
{
    private readonly Func<CitrusSettings, Task> apply;
    public SettingsWindow(CitrusSettings settings, Func<CitrusSettings, Task> apply)
    {
        this.apply = apply;
        InitializeComponent();
        Theme.ItemsSource = Enum.GetValues<ThemeChoice>();
        Shift.ItemsSource = Alt.ItemsSource = Control.ItemsSource = Enum.GetValues<OperationMode>();
        Startup.IsChecked = settings.LaunchAtSignIn;
        Reveal.IsChecked = settings.RevealCompletedOutputs;
        Gestures.IsChecked = settings.GesturesEnabled;
        Theme.SelectedItem = settings.Theme;
        Shift.SelectedItem = settings.ShiftMode;
        Alt.SelectedItem = settings.AltMode;
        Control.SelectedItem = settings.ControlMode;
    }
    private void Cancel(object sender, RoutedEventArgs e) => Close();
    private async void Save(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = new CitrusSettings { LaunchAtSignIn = Startup.IsChecked == true, RevealCompletedOutputs = Reveal.IsChecked == true,
                GesturesEnabled = Gestures.IsChecked == true, Theme = (ThemeChoice)Theme.SelectedItem,
                ShiftMode = (OperationMode)Shift.SelectedItem, AltMode = (OperationMode)Alt.SelectedItem, ControlMode = (OperationMode)Control.SelectedItem };
            settings.Validate();
            await apply(settings);
            Close();
        }
        catch (Exception error) { Error.Text = error.Message; }
    }
}
