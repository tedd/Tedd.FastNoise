using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Media3D;
using Tedd.FastNoise.Designer.Rendering;
using Tedd.FastNoise.Designer.ViewModels;

namespace Tedd.FastNoise.Designer;

/// <summary>
/// The shell window.
/// </summary>
/// <remarks>
/// Everything except the 3D scene graph is bound. The <c>Viewport3D</c> is driven from code because
/// <c>ModelVisual3D.Content</c> is not a bindable dependency property, and because the camera needs
/// an input controller rather than a value.
/// </remarks>
public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();
    private readonly OrbitCamera _orbit;

    /// <summary>Builds the window.</summary>
    public MainWindow()
    {
        InitializeComponent();

        DataContext = _viewModel;
        _orbit = new OrbitCamera(ViewportSurface, Camera);

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        _viewModel.ResetViewRequested += (_, _) => _orbit.Reset();

        // The zoom slider is logarithmic: linear world-units-per-sample would spend nine tenths of
        // its travel between 1 and 100 and leave nothing for the range that matters.
        ZoomSlider.Value = Math.Log2(_viewModel.Step);
        ZoomSlider.ValueChanged += (_, e) => _viewModel.Step = (float)Math.Pow(2, e.NewValue);

        SceneRoot.Content = _viewModel.Model;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Model):
                SceneRoot.Content = _viewModel.Model;
                break;

            case nameof(MainViewModel.Step):
            {
                // Keep the slider in step when the value is changed from the text box or a preset,
                // without feeding the change back round and losing precision.
                double target = Math.Log2(_viewModel.Step);
                if (Math.Abs(ZoomSlider.Value - target) > 0.0001)
                {
                    ZoomSlider.Value = target;
                }

                break;
            }

            default:
                break;
        }
    }

    private void OnZoomPresetClicked(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: float step })
        {
            _viewModel.ApplyZoom(step);
        }
        else if (sender is Button { Tag: string text }
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
        {
            _viewModel.ApplyZoom(parsed);
        }
    }
}
