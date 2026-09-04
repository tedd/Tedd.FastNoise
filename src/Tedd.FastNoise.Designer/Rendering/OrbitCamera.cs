using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Media3D;

namespace Tedd.FastNoise.Designer.Rendering;

/// <summary>
/// Drag to orbit, wheel to zoom, right-drag to pan. Attaches to any element and drives a camera.
/// </summary>
public sealed class OrbitCamera
{
    private readonly PerspectiveCamera _camera;
    private readonly FrameworkElement _surface;

    private double _yaw = -0.9;
    private double _pitch = 0.55;
    private double _distance = 2.4;
    private Vector3D _target;

    private Point _lastPosition;
    private bool _orbiting;
    private bool _panning;

    /// <summary>Wires the controller to an element and a camera.</summary>
    /// <param name="surface">The element that receives the mouse input.</param>
    /// <param name="camera">The camera to drive.</param>
    public OrbitCamera(FrameworkElement surface, PerspectiveCamera camera)
    {
        _surface = surface;
        _camera = camera;

        surface.MouseDown += OnMouseDown;
        surface.MouseUp += OnMouseUp;
        surface.MouseMove += OnMouseMove;
        surface.MouseWheel += OnMouseWheel;
        surface.MouseLeave += (_, _) => StopDragging();

        Apply();
    }

    /// <summary>Returns the camera to its default framing.</summary>
    public void Reset()
    {
        _yaw = -0.9;
        _pitch = 0.55;
        _distance = 2.4;
        _target = default;
        Apply();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        _lastPosition = e.GetPosition(_surface);

        if (e.ChangedButton == MouseButton.Left)
        {
            _orbiting = true;
        }
        else if (e.ChangedButton is MouseButton.Right or MouseButton.Middle)
        {
            _panning = true;
        }

        _surface.CaptureMouse();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e) => StopDragging();

    private void StopDragging()
    {
        _orbiting = false;
        _panning = false;
        _surface.ReleaseMouseCapture();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_orbiting && !_panning)
        {
            return;
        }

        Point position = e.GetPosition(_surface);
        Vector delta = position - _lastPosition;
        _lastPosition = position;

        if (_orbiting)
        {
            _yaw -= delta.X * 0.008;

            // Stop just short of the poles: looking straight down makes the up vector ambiguous
            // and the view snaps through 180 degrees.
            _pitch = Math.Clamp(_pitch + (delta.Y * 0.008), -1.53, 1.53);
        }
        else
        {
            // Pan in the camera's own plane, scaled by distance so it feels the same at any zoom.
            Vector3D right = new(Math.Cos(_yaw), 0, -Math.Sin(_yaw));
            Vector3D up = new(0, 1, 0);
            _target += (right * (-delta.X * 0.0025 * _distance)) + (up * (delta.Y * 0.0025 * _distance));
        }

        Apply();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        _distance = Math.Clamp(_distance * Math.Pow(0.9, e.Delta / 120.0), 0.25, 20.0);
        Apply();
    }

    private void Apply()
    {
        double horizontal = Math.Cos(_pitch) * _distance;

        Vector3D offset = new(
            Math.Sin(_yaw) * horizontal,
            Math.Sin(_pitch) * _distance,
            Math.Cos(_yaw) * horizontal);

        Point3D position = new(_target.X + offset.X, _target.Y + offset.Y, _target.Z + offset.Z);

        _camera.Position = position;
        _camera.LookDirection = new Vector3D(-offset.X, -offset.Y, -offset.Z);
        _camera.UpDirection = new Vector3D(0, 1, 0);
    }
}
