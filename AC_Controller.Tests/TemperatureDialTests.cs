using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;

namespace AC_Controller.Tests;

public sealed class TemperatureDialTests
{
    [Theory]
    [InlineData(int.MinValue, 0)]
    [InlineData(16, 0)]
    [InlineData(23, 0.5)]
    [InlineData(30, 1)]
    [InlineData(int.MaxValue, 1)]
    public void ArcPositionUsesTheSetPointScale(int celsius, double expected)
    {
        Assert.Equal(expected, TemperatureDial.GetProgress(celsius));
    }

    [Fact]
    public async Task AccessibleReadingTracksDisplayedStateAndKeepsOriginalValue()
    {
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var dial = new TemperatureDial();
                var peer = UIElementAutomationPeer.CreatePeerForElement(dial)!;
                Assert.Equal(AutomationControlType.Text, peer.GetAutomationControlType());
                Assert.Contains("Sem conexão", peer.GetName());
                dial.SetReading(24, false);
                Assert.Contains("desligado", peer.GetName());
                dial.SetReading(35, true);
                Assert.Contains("35 graus Celsius", peer.GetName());
                dial.SetReading(null, true);
                Assert.Contains("Sem conexão", peer.GetName());
                Assert.False(dial.Focusable);
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(dial));
                dial.Measure(new Size(130, 90));
                Assert.Equal(new Size(130, 90), dial.DesiredSize);
                finished.SetResult();
            }
            catch (Exception exception)
            {
                finished.SetException(exception);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }
}
