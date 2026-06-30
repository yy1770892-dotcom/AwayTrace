using System.Windows;
using AwayTrace.Core.Services;

namespace AwayTrace.App.Views;

public partial class PinSetupWindow : Window
{
    private readonly PinService _pinService;

    public PinSetupWindow(PinService pinService)
    {
        InitializeComponent();
        _pinService = pinService;
        Loaded += (_, _) => PinBox.Focus();
    }

    private async void OnSaveClick(object sender, RoutedEventArgs e)
    {
        MessageText.Text = string.Empty;
        if (PinBox.Password != ConfirmPinBox.Password)
        {
            MessageText.Text = "PIN과 PIN 확인이 일치하지 않습니다.";
            return;
        }

        try
        {
            await _pinService.SetPinAsync(PinBox.Password);
            DialogResult = true;
        }
        catch (ArgumentException ex)
        {
            MessageText.Text = ex.Message;
        }
    }
}
