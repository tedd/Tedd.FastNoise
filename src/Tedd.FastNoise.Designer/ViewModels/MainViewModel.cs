using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using Tedd.FastNoise.Designer.CodeGen;
using Tedd.FastNoise.Designer.Presets;
using Tedd.FastNoise.Designer.Rendering;

namespace Tedd.FastNoise.Designer.ViewModels;

/// <summary>What the preview pane is showing.</summary>
public enum PreviewMode
{
    /// <summary>The field as a flat image.</summary>
    Map2D = 0,

    /// <summary>The same 2D field, displaced into terrain and rotatable.</summary>
    Heightmap3D = 1,

    /// <summary>A 3D field thresholded into voxels, rotatable.</summary>
    Volume3D = 2,
}

/// <summary>The designer shell: layers, preview settings, live rendering and code generation.</summary>
public sealed class MainViewModel : ObservableObject
{
    private CancellationTokenSource? _inFlight;
    private bool _renderPending;
    private bool _rendering;

    private LayerViewModel? _selectedLayer;
    private PreviewMode _mode = PreviewMode.Map2D;

    private int _resolution = 384;
    private int _volumeResolution = 40;
    private float _originX;
    private float _originY;
    private float _originZ;
    private float _step = 1f;
    private float _threshold;
    private float _heightScale = 0.25f;
    private bool _showThresholdMask;
    private RampKind _ramp = RampKind.Terrain;
    private NoiseBackend _backend = NoiseBackend.Auto;

    // On by default. A design tool that shows an aliased field unless you find the right checkbox
    // teaches the wrong thing about what a zoom level costs and what it should look like.
    private bool _lodEnabled = true;
    private bool _lodCullLayers = true;
    private bool _lodFade = true;
    private float _nyquistFactor = 2f;

    private BitmapSource? _image;
    private Model3D? _model;
    private string _status = "Ready.";
    private string _activeLayers = string.Empty;
    private string _generatedCode = string.Empty;

    /// <summary>Builds the shell with a small example world already loaded.</summary>
    public MainViewModel()
    {
        Layers.CollectionChanged += OnLayersChanged;

        AddLayerCommand = new RelayCommand(AddLayer);
        RemoveLayerCommand = new RelayCommand(RemoveLayer, () => _selectedLayer is not null && Layers.Count > 1);
        DuplicateLayerCommand = new RelayCommand(DuplicateLayer, () => _selectedLayer is not null);
        MoveLayerUpCommand = new RelayCommand(() => MoveLayer(-1), () => IndexOfSelected() > 0);
        MoveLayerDownCommand = new RelayCommand(
            () => MoveLayer(1), () => IndexOfSelected() >= 0 && IndexOfSelected() < Layers.Count - 1);
        ResetViewCommand = new RelayCommand(() => ResetViewRequested?.Invoke(this, EventArgs.Empty));
        CopyCodeCommand = new RelayCommand(CopyCode);
        RefreshCommand = new RelayCommand(() => QueueRender(immediate: true));
        SetModeCommand = new RelayCommand<PreviewMode>(mode => Mode = mode);
        SaveDesignCommand = new RelayCommand(SaveDesign);
        LoadDesignCommand = new RelayCommand(LoadDesign);

        LoadExampleWorld();
    }

    /// <summary>Raised when the user asks for the 3D camera to be re-framed.</summary>
    public event EventHandler? ResetViewRequested;

    /// <summary>The layer stack, coarsest first.</summary>
    public ObservableCollection<LayerViewModel> Layers { get; } = [];

    /// <summary>The layer being edited.</summary>
    public LayerViewModel? SelectedLayer
    {
        get => _selectedLayer;
        set => Set(ref _selectedLayer, value);
    }

    /// <summary>What the preview shows.</summary>
    public PreviewMode Mode
    {
        get => _mode;
        set
        {
            if (Set(ref _mode, value))
            {
                Raise(nameof(Is2D));
                Raise(nameof(Is3D));
                Raise(nameof(IsVolume));
                QueueRender();
            }
        }
    }

    /// <summary>Whether the flat image is showing.</summary>
    public bool Is2D => _mode == PreviewMode.Map2D;

    /// <summary>Whether a rotatable 3D view is showing.</summary>
    public bool Is3D => _mode != PreviewMode.Map2D;

    /// <summary>Whether the volume view is showing.</summary>
    public bool IsVolume => _mode == PreviewMode.Volume3D;

    /// <summary>Edge length in samples of the 2D preview.</summary>
    public int Resolution
    {
        get => _resolution;
        set => SetAndRender(ref _resolution, Math.Clamp(value, 16, 1024));
    }

    /// <summary>Edge length in samples of the 3D volume preview.</summary>
    /// <remarks>
    /// Kept modest on purpose. The mesh is built from exposed voxel faces, and the face count grows
    /// with the square of this while WPF's software scene graph does not.
    /// </remarks>
    public int VolumeResolution
    {
        get => _volumeResolution;
        set => SetAndRender(ref _volumeResolution, Math.Clamp(value, 8, 96));
    }

    /// <summary>World X of the first sample.</summary>
    public float OriginX
    {
        get => _originX;
        set => SetAndRender(ref _originX, value);
    }

    /// <summary>World Y of the first sample.</summary>
    public float OriginY
    {
        get => _originY;
        set => SetAndRender(ref _originY, value);
    }

    /// <summary>World Z of the first sample. Volume preview only.</summary>
    public float OriginZ
    {
        get => _originZ;
        set => SetAndRender(ref _originZ, value);
    }

    /// <summary>
    /// World units between samples: the zoom control.
    /// </summary>
    /// <remarks>
    /// 1 is one sample per block. 1024 is a view from far enough away that a continent fits on
    /// screen. Turn on <see cref="LodEnabled"/> and watch <see cref="ActiveLayers"/> to see what a
    /// given zoom actually costs.
    /// </remarks>
    public float Step
    {
        get => _step;
        set => SetAndRender(ref _step, Math.Max(0.0001f, value));
    }

    /// <summary>Values at or above this count as solid in the volume view and the 2D mask.</summary>
    public float Threshold
    {
        get => _threshold;
        set => SetAndRender(ref _threshold, value);
    }

    /// <summary>
    /// Draws the 2D map as solid-versus-empty at <see cref="Threshold"/> instead of as a gradient.
    /// </summary>
    /// <remarks>
    /// The 2D view of the question the volume view answers: where exactly does the terrain cut?
    /// Much quicker to iterate on than rebuilding a voxel mesh for every tweak.
    /// </remarks>
    public bool ShowThresholdMask
    {
        get => _showThresholdMask;
        set => SetAndRender(ref _showThresholdMask, value);
    }

    /// <summary>Vertical exaggeration of the heightmap mesh.</summary>
    public float HeightScale
    {
        get => _heightScale;
        set => SetAndRender(ref _heightScale, value);
    }

    /// <summary>Colour mapping.</summary>
    public RampKind Ramp
    {
        get => _ramp;
        set => SetAndRender(ref _ramp, value);
    }

    /// <summary>Which backend the preview fill uses. Output is identical either way; only the time differs.</summary>
    public NoiseBackend Backend
    {
        get => _backend;
        set => SetAndRender(ref _backend, value);
    }

    /// <summary>Whether to drop detail the sample grid cannot carry.</summary>
    public bool LodEnabled
    {
        get => _lodEnabled;
        set => SetAndRender(ref _lodEnabled, value);
    }

    /// <summary>Whether to skip layers whose feature size is below the sample spacing.</summary>
    public bool LodCullLayers
    {
        get => _lodCullLayers;
        set => SetAndRender(ref _lodCullLayers, value);
    }

    /// <summary>Whether the finest surviving octave fades in rather than popping.</summary>
    public bool LodFadeLastOctave
    {
        get => _lodFade;
        set => SetAndRender(ref _lodFade, value);
    }

    /// <summary>Samples required per wavelength for an octave to survive.</summary>
    public float NyquistFactor
    {
        get => _nyquistFactor;
        set => SetAndRender(ref _nyquistFactor, Math.Clamp(value, 1f, 16f));
    }

    /// <summary>The rendered 2D image, or null in a 3D mode.</summary>
    public BitmapSource? Image
    {
        get => _image;
        private set => Set(ref _image, value);
    }

    /// <summary>The rendered 3D model, or null in 2D mode.</summary>
    public Model3D? Model
    {
        get => _model;
        private set => Set(ref _model, value);
    }

    /// <summary>Timing and size readout for the last render.</summary>
    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    /// <summary>What the current zoom level actually evaluates.</summary>
    public string ActiveLayers
    {
        get => _activeLayers;
        private set => Set(ref _activeLayers, value);
    }

    /// <summary>C# that reproduces the current configuration.</summary>
    public string GeneratedCode
    {
        get => _generatedCode;
        private set => Set(ref _generatedCode, value);
    }

    /// <summary>Preview modes, for the picker.</summary>
    public static IReadOnlyList<PreviewMode> Modes { get; } = Enum.GetValues<PreviewMode>();

    /// <summary>Colour ramps, for the picker.</summary>
    public static IReadOnlyList<RampKind> Ramps { get; } = Enum.GetValues<RampKind>();

    /// <summary>Backends, for the picker.</summary>
    public static IReadOnlyList<NoiseBackend> Backends { get; } = Enum.GetValues<NoiseBackend>();

    /// <summary>Preset zoom levels, from one sample per block out to orbit.</summary>
    public static IReadOnlyList<float> ZoomPresets { get; } = [0.25f, 1f, 4f, 16f, 64f, 256f, 1024f, 4096f];

    /// <summary>Adds a new layer above the selection.</summary>
    public RelayCommand AddLayerCommand { get; }

    /// <summary>Removes the selected layer.</summary>
    public RelayCommand RemoveLayerCommand { get; }

    /// <summary>Copies the selected layer.</summary>
    public RelayCommand DuplicateLayerCommand { get; }

    /// <summary>Moves the selected layer earlier in the stack.</summary>
    public RelayCommand MoveLayerUpCommand { get; }

    /// <summary>Moves the selected layer later in the stack.</summary>
    public RelayCommand MoveLayerDownCommand { get; }

    /// <summary>Re-frames the 3D camera.</summary>
    public RelayCommand ResetViewCommand { get; }

    /// <summary>Puts the generated code on the clipboard.</summary>
    public RelayCommand CopyCodeCommand { get; }

    /// <summary>Forces an immediate re-render.</summary>
    public RelayCommand RefreshCommand { get; }

    /// <summary>Switches the preview mode. Bound to Ctrl+1 through Ctrl+3.</summary>
    public RelayCommand<PreviewMode> SetModeCommand { get; }

    /// <summary>Writes the whole design to a JSON file.</summary>
    public RelayCommand SaveDesignCommand { get; }

    /// <summary>Replaces the design with one read from a JSON file.</summary>
    public RelayCommand LoadDesignCommand { get; }

    /// <summary>Sets the zoom to one of the presets.</summary>
    /// <param name="step">World units between samples.</param>
    public void ApplyZoom(float step) => Step = step;

    /// <summary>The level-of-detail policy the current settings describe.</summary>
    private LodPolicy CurrentLod => _lodEnabled
        ? new LodPolicy
        {
            CullOctaves = true,
            NyquistFactor = _nyquistFactor,
            FadeLastOctave = _lodFade,
            CullLayers = _lodCullLayers,
        }
        : LodPolicy.Disabled;

    private void LoadExampleWorld()
    {
        Layers.Add(new LayerViewModel
        {
            Name = "continents",
            Seed = 1337,
            NoiseType = NoiseType.OpenSimplex2,
            FractalType = FractalType.FBm,
            Octaves = 5,
            Frequency = 0.004f,
            Amplitude = 1f,
            FeatureSize = 400f,
        });

        Layers.Add(new LayerViewModel
        {
            Name = "mountains",
            Seed = 90210,
            NoiseType = NoiseType.OpenSimplex2,
            FractalType = FractalType.Ridged,
            Octaves = 5,
            Frequency = 0.012f,
            Blend = LayerBlend.Add,
            Amplitude = 0.35f,
            FeatureSize = 80f,
        });

        Layers.Add(new LayerViewModel
        {
            Name = "surface detail",
            Seed = 555,
            NoiseType = NoiseType.Value,
            FractalType = FractalType.None,
            Frequency = 0.12f,
            Blend = LayerBlend.Add,
            Amplitude = 0.05f,
            FeatureSize = 8f,
        });

        SelectedLayer = Layers[0];
        QueueRender(immediate: true);
    }

    private void OnLayersChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (LayerViewModel layer in e.NewItems?.OfType<LayerViewModel>() ?? [])
        {
            layer.Changed += OnLayerChanged;
        }

        foreach (LayerViewModel layer in e.OldItems?.OfType<LayerViewModel>() ?? [])
        {
            layer.Changed -= OnLayerChanged;
        }

        QueueRender();
    }

    private void OnLayerChanged(object? sender, EventArgs e) => QueueRender();

    private void AddLayer()
    {
        LayerViewModel layer = new()
        {
            Name = $"layer {Layers.Count + 1}",
            Seed = Random.Shared.Next(),
            Amplitude = 0.5f,
        };

        Layers.Add(layer);
        SelectedLayer = layer;
    }

    private void RemoveLayer()
    {
        if (_selectedLayer is null || Layers.Count <= 1)
        {
            return;
        }

        int index = Layers.IndexOf(_selectedLayer);
        Layers.Remove(_selectedLayer);
        SelectedLayer = Layers[Math.Clamp(index, 0, Layers.Count - 1)];
    }

    private void DuplicateLayer()
    {
        if (_selectedLayer is null)
        {
            return;
        }

        LayerViewModel copy = _selectedLayer.Clone();
        Layers.Insert(Layers.IndexOf(_selectedLayer) + 1, copy);
        SelectedLayer = copy;
    }

    private int IndexOfSelected() => _selectedLayer is null ? -1 : Layers.IndexOf(_selectedLayer);

    private void MoveLayer(int delta)
    {
        int index = IndexOfSelected();
        int target = index + delta;

        if (index < 0 || target < 0 || target >= Layers.Count)
        {
            return;
        }

        Layers.Move(index, target);
        QueueRender();
    }

    /// <summary>Captures the current design.</summary>
    private DesignDocument Capture()
    {
        DesignDocument document = new()
        {
            View = new ViewDocument
            {
                Mode = _mode,
                Ramp = _ramp,
                OriginX = _originX,
                OriginY = _originY,
                OriginZ = _originZ,
                Step = _step,
                Resolution = _resolution,
                VolumeResolution = _volumeResolution,
                Threshold = _threshold,
                HeightScale = _heightScale,
                ShowThresholdMask = _showThresholdMask,
                LodEnabled = _lodEnabled,
                LodCullLayers = _lodCullLayers,
                LodFadeLastOctave = _lodFade,
                NyquistFactor = _nyquistFactor,
            },
        };

        foreach (LayerViewModel layer in Layers)
        {
            document.Layers.Add(LayerDocument.From(layer));
        }

        return document;
    }

    private void SaveDesign()
    {
        Microsoft.Win32.SaveFileDialog dialog = new()
        {
            Title = "Save design",
            Filter = "FastNoise design (*.fnoise.json)|*.fnoise.json|JSON (*.json)|*.json",
            DefaultExt = ".fnoise.json",
            FileName = "design.fnoise.json",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            Capture().Save(dialog.FileName);
            Status = $"Saved to {System.IO.Path.GetFileName(dialog.FileName)}.";
        }
        catch (Exception exception) when (exception is System.IO.IOException or UnauthorizedAccessException)
        {
            Status = $"Could not save: {exception.Message}";
        }
    }

    private void LoadDesign()
    {
        Microsoft.Win32.OpenFileDialog dialog = new()
        {
            Title = "Open design",
            Filter = "FastNoise design (*.fnoise.json)|*.fnoise.json|JSON (*.json)|*.json|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        DesignDocument document;

        try
        {
            document = DesignDocument.Load(dialog.FileName);
        }
        catch (Exception exception)
            when (exception is System.IO.IOException or System.Text.Json.JsonException or System.IO.InvalidDataException)
        {
            Status = $"Could not open: {exception.Message}";
            return;
        }

        if (document.Layers.Count == 0)
        {
            Status = "That design has no layers.";
            return;
        }

        Apply(document);
        Status = $"Opened {System.IO.Path.GetFileName(dialog.FileName)}.";
    }

    /// <summary>Replaces the current design, rendering once at the end rather than per property.</summary>
    private void Apply(DesignDocument document)
    {
        Layers.CollectionChanged -= OnLayersChanged;

        foreach (LayerViewModel existing in Layers)
        {
            existing.Changed -= OnLayerChanged;
        }

        Layers.Clear();

        foreach (LayerDocument layer in document.Layers)
        {
            LayerViewModel viewModel = layer.ToViewModel();
            viewModel.Changed += OnLayerChanged;
            Layers.Add(viewModel);
        }

        Layers.CollectionChanged += OnLayersChanged;

        ViewDocument view = document.View;
        _mode = view.Mode;
        _ramp = view.Ramp;
        _originX = view.OriginX;
        _originY = view.OriginY;
        _originZ = view.OriginZ;
        _step = Math.Max(0.0001f, view.Step);
        _resolution = Math.Clamp(view.Resolution, 16, 1024);
        _volumeResolution = Math.Clamp(view.VolumeResolution, 8, 96);
        _threshold = view.Threshold;
        _heightScale = view.HeightScale;
        _showThresholdMask = view.ShowThresholdMask;
        _lodEnabled = view.LodEnabled;
        _lodCullLayers = view.LodCullLayers;
        _lodFade = view.LodFadeLastOctave;
        _nyquistFactor = Math.Clamp(view.NyquistFactor, 1f, 16f);

        // One blanket notification: the whole object changed, and enumerating every property name
        // here would be a list to forget to update.
        Raise(null);
        SelectedLayer = Layers[0];
        QueueRender();
    }

    private void CopyCode()
    {
        try
        {
            Clipboard.SetText(_generatedCode);
            Status = "Code copied to clipboard.";
        }
        catch (COMException)
        {
            // Another process can hold the clipboard open; not worth surfacing as an error dialog.
            Status = "Could not reach the clipboard. Select the code and copy it manually.";
        }
    }

    private bool SetAndRender<T>(
        ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string? propertyName = null)
    {
        if (!Set(ref field, value, propertyName))
        {
            return false;
        }

        QueueRender();
        return true;
    }

    /// <summary>
    /// Marks the preview dirty and makes sure a render is running.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Coalescing, not debouncing. A debounce timer restarted on every change never fires at all
    /// while a slider is being dragged, so the preview freezes until the mouse stops -- which is
    /// exactly when you least want it to, because dragging is how you find the value you want.
    /// </para>
    /// <para>
    /// Instead: render immediately, and if changes arrived while that render was running, render
    /// once more when it finishes. Updates then arrive as fast as the machine can produce them and
    /// no faster, and no work is queued up behind a drag that has already moved on.
    /// </para>
    /// </remarks>
    private void QueueRender(bool immediate = false)
    {
        _renderPending = true;

        if (!_rendering)
        {
            _ = RenderLoopAsync();
        }
    }

    /// <summary>Renders until nothing is dirty. Runs on the UI thread, so the flags need no locking.</summary>
    private async Task RenderLoopAsync()
    {
        _rendering = true;

        try
        {
            while (_renderPending)
            {
                _renderPending = false;
                await RenderAsync().ConfigureAwait(true);
            }
        }
        finally
        {
            _rendering = false;
        }
    }

    private async Task RenderAsync()
    {
        _inFlight?.Cancel();
        CancellationTokenSource cancellation = new();
        _inFlight = cancellation;
        CancellationToken token = cancellation.Token;

        // Snapshot everything the worker needs, so edits made while it runs cannot tear the render.
        List<LayerViewModel> enabled = Layers.Where(static layer => layer.IsEnabled).ToList();
        LodPolicy lod = CurrentLod;
        PreviewMode mode = _mode;
        int resolution = _resolution;
        int volumeResolution = _volumeResolution;
        float originX = _originX, originY = _originY, originZ = _originZ;
        float step = _step, threshold = _threshold, heightScale = _heightScale;
        bool showMask = _showThresholdMask;
        RampKind ramp = _ramp;
        NoiseBackend backend = _backend;

        PreviewRegion previewRegion = mode == PreviewMode.Volume3D
            ? new PreviewRegion(originX, originY, originZ, volumeResolution, volumeResolution, volumeResolution, step)
            : new PreviewRegion(originX, originY, originZ, resolution, resolution, 1, step);

        GeneratedCode = CSharpEmitter.Emit(Layers, lod, previewRegion, mode == PreviewMode.Volume3D);

        if (enabled.Count == 0)
        {
            Image = null;
            Model = null;
            Status = "No layers enabled.";
            ActiveLayers = string.Empty;
            return;
        }

        try
        {
            RenderResult result = await Task.Run(
                () => Render(enabled, lod, mode, resolution, volumeResolution,
                    originX, originY, originZ, step, threshold, heightScale, showMask, ramp, backend, token),
                token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            Image = result.Image;
            Model = result.Model;
            Status = result.Status;
            ActiveLayers = result.ActiveLayers;
        }
        catch (OperationCanceledException)
        {
            // A newer render superseded this one. Nothing to report.
        }
        catch (Exception exception)
        {
            Status = $"Render failed: {exception.Message}";
        }
    }

    private static RenderResult Render(
        List<LayerViewModel> layers,
        LodPolicy lod,
        PreviewMode mode,
        int resolution,
        int volumeResolution,
        float originX,
        float originY,
        float originZ,
        float step,
        float threshold,
        float heightScale,
        bool showThresholdMask,
        RampKind ramp,
        NoiseBackend backend,
        CancellationToken token)
    {
        NoiseStack stack = new() { Lod = lod };
        foreach (LayerViewModel layer in layers)
        {
            stack.Add(layer.BuildLayer());
        }

        CompiledNoiseStack compiled = stack.Compile();
        token.ThrowIfCancellationRequested();

        string activeLayers = string.Join(
            Environment.NewLine,
            compiled.DescribeActiveLayers(step).Select(static description => "- " + description));

        if (activeLayers.Length == 0)
        {
            activeLayers = "- nothing survives level-of-detail culling at this zoom";
        }

        Stopwatch clock = Stopwatch.StartNew();

        if (mode == PreviewMode.Volume3D)
        {
            GridRegion3D region = new(
                originX, originY, originZ, volumeResolution, volumeResolution, volumeResolution, step);

            float[] field = new float[region.SampleCount];
            compiled.Fill(field, region, backend);
            double fillMilliseconds = clock.Elapsed.TotalMilliseconds;
            token.ThrowIfCancellationRequested();

            MeshGeometry3D mesh = VoxelMesh.Build(
                field, region.Width, region.Height, region.Depth, threshold, out int faceCount);

            return new RenderResult(
                Image: null,
                Model: BuildModel(mesh, ramp),
                Status: FormatStatus(region.SampleCount, fillMilliseconds, clock.Elapsed.TotalMilliseconds,
                    $"{faceCount:N0} exposed faces"),
                ActiveLayers: activeLayers);
        }

        GridRegion2D region2D = new(originX, originY, resolution, resolution, step);
        float[] field2D = new float[region2D.SampleCount];
        compiled.Fill(field2D, region2D, backend);
        double fill2DMilliseconds = clock.Elapsed.TotalMilliseconds;
        token.ThrowIfCancellationRequested();

        if (mode == PreviewMode.Heightmap3D)
        {
            // Cap the mesh independently of the field: WPF's scene graph is the bottleneck, not the noise.
            const int MaxMeshEdge = 192;
            int meshEdge = Math.Min(resolution, MaxMeshEdge);
            float[] meshField = meshEdge == resolution ? field2D : Downsample(field2D, resolution, meshEdge);

            MeshGeometry3D mesh = HeightmapMesh.Build(meshField, meshEdge, meshEdge, heightScale);

            return new RenderResult(
                Image: null,
                Model: BuildModel(mesh, ramp),
                Status: FormatStatus(region2D.SampleCount, fill2DMilliseconds, clock.Elapsed.TotalMilliseconds,
                    $"mesh {meshEdge}x{meshEdge}"),
                ActiveLayers: activeLayers);
        }

        BitmapSource image = FieldImage.Render(
            field2D, resolution, resolution, ramp, threshold, showThresholdMask);

        return new RenderResult(
            Image: image,
            Model: null,
            Status: FormatStatus(region2D.SampleCount, fill2DMilliseconds, clock.Elapsed.TotalMilliseconds, null),
            ActiveLayers: activeLayers);
    }

    /// <summary>Point-samples a square field down to a smaller square.</summary>
    private static float[] Downsample(float[] field, int sourceEdge, int targetEdge)
    {
        float[] result = new float[targetEdge * targetEdge];

        for (int y = 0; y < targetEdge; y++)
        {
            int sourceY = y * sourceEdge / targetEdge;

            for (int x = 0; x < targetEdge; x++)
            {
                result[x + (y * targetEdge)] = field[(x * sourceEdge / targetEdge) + (sourceY * sourceEdge)];
            }
        }

        return result;
    }

    private static Model3D BuildModel(MeshGeometry3D mesh, RampKind ramp)
    {
        MaterialGroup material = new();
        material.Children.Add(new DiffuseMaterial(ColourRamp.BuildBrush(ramp)));
        material.Children.Add(new SpecularMaterial(new SolidColorBrush(Color.FromRgb(40, 40, 40)), 18));
        material.Freeze();

        GeometryModel3D geometry = new(mesh, material)
        {
            BackMaterial = material,
        };

        Model3DGroup group = new();
        group.Children.Add(geometry);
        group.Children.Add(new AmbientLight(Color.FromRgb(70, 70, 78)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(210, 208, 200), new Vector3D(-0.6, -1, -0.45)));
        group.Children.Add(new DirectionalLight(Color.FromRgb(60, 66, 82), new Vector3D(0.7, 0.3, 0.6)));
        group.Freeze();

        return group;
    }

    private static string FormatStatus(int samples, double fillMilliseconds, double totalMilliseconds, string? extra)
    {
        double samplesPerSecond = fillMilliseconds > 0 ? samples / (fillMilliseconds / 1000.0) : 0;

        string text = string.Create(
            CultureInfo.InvariantCulture,
            $"{samples:N0} samples in {fillMilliseconds:0.0} ms ({samplesPerSecond / 1_000_000:0.0} M/s), {totalMilliseconds:0.0} ms including display");

        return extra is null ? text : $"{text} - {extra}";
    }

    private sealed record RenderResult(BitmapSource? Image, Model3D? Model, string Status, string ActiveLayers);
}
