using System.Runtime.ExceptionServices;
using System.Windows.Controls;
using MicMeter.Audio;
using MicMeter.Models;

namespace MicMeter.Tests;

public sealed class SettingsLocalizationTests
{
    [Fact]
    public void SettingsWindow_CanSwitchBetweenEnglishAndJapanese()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var settings = new AppSettings
                {
                    DeviceIds = ["test-device"],
                    UiLanguage = AppLanguage.English
                };
                var window = new SettingsWindow(settings,
                    [new AudioDeviceInfo("test-device", "Studio Microphone")]);

                Assert.Equal("MicMeter Settings", window.Title);
                Assert.Equal("Tray meter",
                    ((CheckBox)window.FindName("ShowTrayMeterCheckBox")).Content);
                Assert.Equal("Mute/unmute sounds",
                    ((CheckBox)window.FindName("PlayMuteSoundsCheckBox")).Content);
                Assert.Equal("Mute overlay",
                    ((CheckBox)window.FindName("ShowMuteOverlayCheckBox")).Content);
                Assert.Equal("Position mute overlay", FindButton(window, "Position mute overlay").Content);
                Assert.Equal("Save", FindButton(window, "Save").Content);
                Assert.Equal("Cancel", FindButton(window, "Cancel").Content);

                ((ComboBox)window.FindName("UiLanguageComboBox")).SelectedIndex = 0;
                Assert.Equal("MicMeter 設定", window.Title);
                Assert.Equal("トレイメーター",
                    ((CheckBox)window.FindName("ShowTrayMeterCheckBox")).Content);
                window.Close();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)), "The WPF localization test timed out.");
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private static Button FindButton(SettingsWindow window, string content)
    {
        return FindLogicalChildren<Button>(window).Single(button => Equals(button.Content, content));
    }

    private static IEnumerable<T> FindLogicalChildren<T>(System.Windows.DependencyObject parent)
        where T : System.Windows.DependencyObject
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent).OfType<System.Windows.DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindLogicalChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}
