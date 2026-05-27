using System;
using System.Collections.Specialized;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.VisualTree;
using Avalonia.Threading;
using FsZbGroundApp.ViewModels;

namespace FsZbGroundApp.Views;

public partial class MainView : UserControl
{
    private const double CompactBreakpoint = 980;
    private readonly Grid _layoutGrid;
    private readonly Border _controlPanel;
    private readonly ListBox _driverLogList;
    private MainViewModel? _viewModel;

    public MainView()
    {
        InitializeComponent();

        _layoutGrid = this.FindControl<Grid>("LayoutGrid")
            ?? throw new InvalidOperationException("LayoutGrid was not found.");
        _controlPanel = this.FindControl<Border>("ControlPanel")
            ?? throw new InvalidOperationException("ControlPanel was not found.");
        _driverLogList = this.FindControl<ListBox>("DriverLogList")
            ?? throw new InvalidOperationException("DriverLogList was not found.");

        UpdateLayoutMode(Bounds.Width);
        SizeChanged += OnSizeChanged;
        DataContextChanged += OnDataContextChanged;
        OnDataContextChanged(this, EventArgs.Empty);
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        UpdateLayoutMode(e.NewSize.Width);
    }

    private void UpdateLayoutMode(double width)
    {
        var compact = width < CompactBreakpoint;

        if (compact)
        {
            _layoutGrid.ColumnDefinitions = new ColumnDefinitions("*");
            _layoutGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
            Grid.SetColumn(_controlPanel, 0);
            Grid.SetRow(_controlPanel, 1);
            _controlPanel.Margin = new Thickness(0, 16, 0, 0);
        }
        else
        {
            _layoutGrid.ColumnDefinitions = new ColumnDefinitions("2*,420");
            _layoutGrid.RowDefinitions = new RowDefinitions("Auto");
            Grid.SetColumn(_controlPanel, 1);
            Grid.SetRow(_controlPanel, 0);
            _controlPanel.Margin = new Thickness(0);
        }

        PseudoClasses.Set(":compact", compact);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.DriverLogs.CollectionChanged -= OnDriverLogsCollectionChanged;
        }

        _viewModel = DataContext as MainViewModel;

        if (_viewModel is not null)
        {
            _viewModel.DriverLogs.CollectionChanged += OnDriverLogsCollectionChanged;
        }
    }

    private void OnDriverLogsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || _viewModel is null || _viewModel.DriverLogs.Count == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => _driverLogList.ScrollIntoView(_viewModel.DriverLogs[^1]));
    }

    private async void OnDriverLogSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_driverLogList.SelectedItem is not GroundLogEntry entry)
        {
            return;
        }

        await CopyLogEntryToClipboardAsync(entry);
        _driverLogList.SelectedItem = null;
    }

    private async Task CopyLogEntryToClipboardAsync(GroundLogEntry entry)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.Clipboard is null)
        {
            return;
        }

        await topLevel.Clipboard.SetTextAsync(entry.DisplayText);
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.DriverLogs.CollectionChanged -= OnDriverLogsCollectionChanged;
        }

        DataContextChanged -= OnDataContextChanged;
        base.OnDetachedFromVisualTree(e);
    }
}