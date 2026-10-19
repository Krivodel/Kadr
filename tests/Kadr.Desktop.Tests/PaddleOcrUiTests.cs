using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using Kadr.Desktop.Services.Settings;
using Kadr.Desktop.ViewModels;
using Kadr.Desktop.Views;
using Kadr.Desktop.Views.Settings;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Kadr.Desktop.Tests;

[Collection("Avalonia UI")]
public sealed class PaddleOcrUiTests
{
    [Fact]
    public async Task Recognition_SingleEngine_HasNoEngineChoiceAndPreservesLanguages()
    {
        await using HeadlessUnitTestSession session = HeadlessUnitTestSession.StartNew(typeof(UiTestSession));
        await session.Dispatch(async () =>
        {
            TestSettingsApplication application = new();
            SettingsViewModel model = new(UiTestSession.CreateSettingsStore(), application,
                NullLogger<SettingsViewModel>.Instance, true) { Section = SettingsSection.Recognition };
            SettingsWindow window = new() { DataContext = model };
            try
            {
                window.Show();
                await UiTestSession.PumpFramesAsync(80);
                RecognitionSettingsView view = window.GetVisualDescendants().OfType<RecognitionSettingsView>().Single();
                Assert.Empty(view.GetVisualDescendants().OfType<ComboBox>());
                UiTestSession.SaveFrame(window, "paddle-only-settings.png");
                await model.SaveCommand.ExecuteAsync(null);

                Assert.Equal(4, model.VisibleLanguages.Count);
                Assert.NotNull(application.AppliedSettings);
                Assert.Equal(new string[] { "eng", "rus" }, application.AppliedSettings.OcrLanguages.Order().ToArray());
                Assert.Null(model.ErrorMessage);
            }
            finally
            {
                window.Close();
            }
            return true;
        }, CancellationToken.None);
    }
}
