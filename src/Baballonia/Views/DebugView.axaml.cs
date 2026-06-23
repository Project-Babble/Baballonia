using Avalonia;
using Avalonia.Controls;
using Avalonia.Rendering.Composition;
using Baballonia.ViewModels.SplitViewPane;

namespace Baballonia.Views;

public partial class DebugView : UserControl
{
    private Compositor? _compositor;
    private bool _running;

    public DebugView()
    {
        InitializeComponent();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _compositor = ElementComposition.GetElementVisual(this)?.Compositor;
        _running = true;
        _compositor?.RequestCompositionUpdate(OnRenderFrame);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _running = false;
        _compositor = null;
    }

    // Re-arms each compositor frame to count Avalonia's render-thread fps while the page is visible.
    private void OnRenderFrame()
    {
        if (!_running)
            return;
        if (DataContext is DebugViewModel vm)
            vm.RenderTicks++;
        _compositor?.RequestCompositionUpdate(OnRenderFrame);
    }
}
