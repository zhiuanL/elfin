using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DesktopPet.Application.Characters;
using DesktopPet.Application.Localization;
using DesktopPet.Application.Windows;
using Microsoft.Win32;

namespace DesktopPet.Windows.Characters;

public sealed class WindowsCharacterPackagePicker(IUiDispatcher dispatcher, ITextLocalizer text) : ICharacterPackagePicker
{
    public async Task<string?> PickAsync(CharacterPackageSourceKind kind, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(kind)) throw new ArgumentOutOfRangeException(nameof(kind));
        // Publish the command's pending Task before entering a nested native modal message loop.
        await Task.Yield();
        string? selected = null;
        await dispatcher.InvokeAsync(() =>
        {
            ct.ThrowIfCancellationRequested();
            var owner = GetActiveWindow();
            if (owner == 0 || HwndSource.FromHwnd(owner)?.RootVisual is not Window ownerWindow)
                throw new InvalidOperationException("Package selection requires an active application window.");
            var initialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var ui = Dispatcher.CurrentDispatcher;
            var showing = true;
            using var cancellation = ct.Register(() => ui.BeginInvoke(DispatcherPriority.Send, new Action(() =>
            {
                if (showing) CloseOwnedDialog(owner);
            })));
            try
            {
                if (kind == CharacterPackageSourceKind.Zip)
                {
                    var dialog = new OpenFileDialog
                    {
                        Title = text.Get(TextKey.CharacterBrowseZip), Filter = text.Get(TextKey.CharacterZipFilter),
                        DefaultExt = ".zip", Multiselect = false, CheckFileExists = true, CheckPathExists = true,
                        AddToRecent = false, DereferenceLinks = false, InitialDirectory = initialDirectory
                    };
                    if (dialog.ShowDialog(ownerWindow) == true) selected = dialog.FileName;
                }
                else
                {
                    var dialog = new OpenFolderDialog
                    {
                        Title = text.Get(TextKey.CharacterBrowseFolder), Multiselect = false,
                        AddToRecent = false, DereferenceLinks = false, InitialDirectory = initialDirectory
                    };
                    if (dialog.ShowDialog(ownerWindow) == true) selected = dialog.FolderName;
                }
            }
            finally { showing = false; }
            return Task.CompletedTask;
        }, ct);
        ct.ThrowIfCancellationRequested();
        return selected;
    }

    private static void CloseOwnedDialog(nint owner)
    {
        const uint ownerCommand = 4; // GW_OWNER
        const uint closeMessage = 0x0010; // WM_CLOSE
        var owned = new List<nint>();
        var thread = GetWindowThreadProcessId(owner, out _);
        if (thread == 0) return; // Owner has already closed.
        // WPF may itself have a hidden owner, so GetLastActivePopup(owner) can return owner
        // even while its file dialog is open. Inspect this UI thread's ownership instead.
        EnumThreadWindows(thread, (window, _) =>
        {
            var className = new System.Text.StringBuilder(64);
            GetClassName(window, className, className.Capacity);
            if (className.ToString() != "#32770") return true; // Standard native dialogs only.
            var ancestor = GetWindow(window, ownerCommand);
            for (var depth = 0; ancestor != 0 && depth < 32; depth++, ancestor = GetWindow(ancestor, ownerCommand))
                if (ancestor == owner) { owned.Add(window); break; }
            return true;
        }, 0);
        foreach (var window in owned)
            if (!PostMessage(window, closeMessage, 0, 0))
            {
                var error = Marshal.GetLastWin32Error();
                if (error != 1400) throw new Win32Exception(error); // A nested dialog may already have closed.
            }
    }
    [DllImport("user32.dll")] private static extern nint GetActiveWindow();
    private delegate bool WindowCallback(nint window, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumThreadWindows(uint thread, WindowCallback callback, nint parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode)]
    private static extern int GetClassName(nint window, System.Text.StringBuilder name, int count);
    [DllImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);
}
