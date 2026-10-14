using SystemResourceMonitor.Desktop.Bar;

namespace SystemResourceMonitor.Desktop.Tests;

public sealed class TrayMenuOwnerTests
{
    private const nint Owner = 0x100;
    private const nint OtherApplication = 0x200;
    private const nint ApplicationChosenDuringMenu = 0x300;

    [Fact]
    public void Menu_uses_the_hidden_owner_and_returns_foreground_to_the_previous_application()
    {
        var native = new FakeNative { Foreground = OtherApplication };
        var owner = new TrayMenuOwner(native);
        owner.Start();

        var command = owner.Show(0x42, default);

        Assert.Equal(1002u, command);
        Assert.Equal(["set-foreground:256", "track:256", "post-null:256", "set-foreground:512"], native.Events);
        Assert.Equal(OtherApplication, native.Foreground);
    }

    [Fact]
    public void Foreground_chosen_by_the_user_while_the_menu_was_open_is_not_overridden()
    {
        var native = new FakeNative { Foreground = OtherApplication, ForegroundAfterTrack = ApplicationChosenDuringMenu };
        var owner = new TrayMenuOwner(native);
        owner.Start();

        _ = owner.Show(0x42, default);

        Assert.Equal(ApplicationChosenDuringMenu, native.Foreground);
        Assert.DoesNotContain("set-foreground:512", native.Events);
    }

    [Fact]
    public void Refused_foreground_still_shows_the_menu_and_does_not_reassign_foreground()
    {
        var native = new FakeNative { Foreground = OtherApplication, RefuseForeground = true };
        var owner = new TrayMenuOwner(native);
        owner.Start();

        Assert.Equal(1002u, owner.Show(0x42, default));
        Assert.Equal(["set-foreground:256", "track:256", "post-null:256"], native.Events);
        Assert.Equal(OtherApplication, native.Foreground);
    }

    [Fact]
    public void Owner_window_is_destroyed_once()
    {
        var native = new FakeNative();
        var owner = new TrayMenuOwner(native);
        owner.Start();

        owner.Remove();
        owner.Remove();

        Assert.Equal(1, native.DestroyCount);
    }

    private sealed class FakeNative : ITrayMenuNativeApi
    {
        public List<string> Events { get; } = [];

        public nint Foreground { get; set; }

        public nint? ForegroundAfterTrack { get; init; }

        public bool RefuseForeground { get; init; }

        public int DestroyCount { get; private set; }

        public nint CreateOwner() => Owner;

        public bool DestroyOwner(nint hwnd)
        {
            DestroyCount++;
            return true;
        }

        public nint GetForeground() => Foreground;

        public bool SetForeground(nint hwnd)
        {
            Events.Add($"set-foreground:{hwnd}");
            if (RefuseForeground)
            {
                return false;
            }

            Foreground = hwnd;
            return true;
        }

        public uint Track(nint menu, NativeMethods.POINT point, nint owner)
        {
            Events.Add($"track:{owner}");
            if (ForegroundAfterTrack is { } chosen)
            {
                Foreground = chosen;
            }

            return 1002;
        }

        public bool PostNull(nint hwnd)
        {
            Events.Add($"post-null:{hwnd}");
            return true;
        }
    }
}
