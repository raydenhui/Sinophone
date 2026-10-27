using FFXIVClientStructs.FFXIV.Component.GUI;
using Sinophone.Handlers;

namespace Sinophone.IntegrationTests;

/// <summary>
/// Guards the check that decides whether an AtkValue carries text. The game sends
/// dialogue as ManagedString (Managed | String), so accepting only String dropped
/// every line and hid the overlay.
/// </summary>
public class TalkHandlerAtkValueTypeTests
{
    [Theory]
    [InlineData(AtkValueType.String, true)]
    [InlineData(AtkValueType.ManagedString, true)]
    [InlineData(AtkValueType.Int, false)]
    [InlineData(AtkValueType.UInt, false)]
    [InlineData(AtkValueType.Bool, false)]
    [InlineData(AtkValueType.Float, false)]
    public void IsStringValue_AcceptsBothStringKinds(AtkValueType type, bool expected)
    {
        Assert.Equal(expected, TalkHandler.IsStringValue(type));
    }
}
