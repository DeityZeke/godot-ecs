#nullable enable

using Godot;
using UltraSim.ECS;
using Client.UI;

namespace Client.ECS.ControlPanel.Sections;

/// <summary>
/// Placeholder panel shown while the legacy stress-test suite is offline.
/// </summary>
[ControlPanelSection(defaultOrder: 20, defaultExpanded: false)]
public partial class StressTestPanel : UIBuilder, IControlPanelSection
{
    public string Title => "Stress Tests";
    public string Id => "stress_test_panel";
    public bool IsExpanded { get; set; }

    public StressTestPanel(World world)
    {
        // Intentionally no-op for now; stress test harness was removed with the CommandBuffer.
    }

    public Control? CreateHeaderButtons() => null;

    public Control CreateUI()
    {
        var mainVBox = CreateVBox(separation: 12);

        mainVBox.AddChild(CreateLabel(
            "The legacy ECS stress-test suite relied on CommandBuffer and has been removed.",
            fontSize: 11,
            color: new Color(0.7f, 0.7f, 0.7f)));

        mainVBox.AddChild(CreateLabel(
            "This panel will return once the deferred queue rewrite lands and the new scenarios are implemented.",
            fontSize: 10,
            color: new Color(0.6f, 0.6f, 0.6f)));

        mainVBox.AddChild(CreateLabel(
            "Status: Stress tests unavailable",
            fontSize: 12,
            color: new Color(1.0f, 0.8f, 0.3f)));

        return mainVBox;
    }

    public void Update(double delta) { }

    public void OnShow() { }

    public void OnHide() { }
}
