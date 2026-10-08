// Copyright (c) 2026 Neil Colvin. MIT licensed.
namespace CrestronHomeNUnit.Mac;

/// <summary>Observed Crestron Home 4.12 controls. Unknown or ambiguous layouts fail without coordinate guesses.</summary>
public sealed class MacHomeNavigation(MacTestSession session)
{
    public static MacSelector HomeTab { get; } = new("XCUIElementTypeButton", "TabBar_tabBarItem_Home");
    public static MacSelector RoomsTab { get; } = new("XCUIElementTypeButton", "TabBar_tabBarItem_Rooms");
    public static MacSelector Room(string name) => new("XCUIElementTypeStaticText", "rooms_roomCardView_title", name);

    public async Task HomeAsync(CancellationToken token = default)
    {
        if (!(await session.ReadAsync(token).ConfigureAwait(false)).Require(HomeTab).Selected)
            await session.ClickAsync(HomeTab, token).ConfigureAwait(false);
        await session.WaitAsync(h => h.Contains(HomeTab) && h.Require(HomeTab).Selected,
            TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
        await session.CaptureAsync("home", token).ConfigureAwait(false);
    }

    public async Task OpenRoomAsync(string name, CancellationToken token = default)
    {
        await HomeAsync(token).ConfigureAwait(false);
        await session.ClickAsync(RoomsTab, token).ConfigureAwait(false);
        await session.WaitAsync(h => h.Contains(Room(name)), TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
        await session.ClickAsync(Room(name), token).ConfigureAwait(false);
        await session.WaitAsync(h => h.Contains(new("XCUIElementTypeButton", "rooms_roomDetails_backButton")),
            TimeSpan.FromSeconds(20), token).ConfigureAwait(false);
        await session.CaptureAsync("room", token).ConfigureAwait(false);
    }
}
