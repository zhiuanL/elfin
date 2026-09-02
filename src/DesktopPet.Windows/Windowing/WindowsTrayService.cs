using System.Drawing;
using System.Windows.Forms;
using DesktopPet.Application.Commands;
using DesktopPet.Application.Contracts;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Windows;
using DesktopPet.Domain.Platform;

namespace DesktopPet.Windows.Windowing;

public sealed class WindowsTrayService(ITextLocalizer text) : ITrayService, INotificationService
{
    private NotifyIcon? _icon;
    private Icon? _image;
    private ContextMenuStrip? _menu;
    private bool _disposed;
    public bool IsVisible => _icon?.Visible == true;
    public event EventHandler<WindowCommandEventArgs>? CommandRequested;
    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_icon is not null) return;
        _menu = new ContextMenuStrip();
        foreach (var item in TrayMenuDefinition.Create().Concat(TrayMenuDefinition.InputItems()))
        {
            var entry = new ToolStripMenuItem(text.Get(item.Label));
            entry.Click += (_, _) => CommandRequested?.Invoke(this, new(item.Command));
            _menu.Items.Add(entry);
        }
        _image = (Icon)SystemIcons.Application.Clone();
        _icon = new NotifyIcon { Text = text.Get(TextKey.PetTitle), Icon = _image, ContextMenuStrip = _menu };
        _icon.DoubleClick += OnDoubleClick;
        _icon.Visible = true;
    }
    public void ShowContextMenu(PixelPoint position)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_menu is null) throw new InvalidOperationException("Tray not started.");
        _menu.Show(new Point(checked((int)Math.Round(position.X)), checked((int)Math.Round(position.Y))));
    }
    private void OnDoubleClick(object? sender, EventArgs e) => CommandRequested?.Invoke(this, new(CommandId.OpenControlCenter));
    public Task ShowAsync(string title, string message, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        Start();
        _icon!.ShowBalloonTip(5000, title, message, ToolTipIcon.Info);
        return Task.CompletedTask;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_icon is not null)
        {
            _icon.Visible = false;
            _icon.DoubleClick -= OnDoubleClick;
            _icon.Dispose();
        }
        _menu?.Dispose();
        _image?.Dispose();
    }
}
