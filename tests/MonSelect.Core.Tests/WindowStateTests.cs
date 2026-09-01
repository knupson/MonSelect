using MonSelect.Core.Windows;

namespace MonSelect.Core.Tests;

public class WindowStateTests
{
    [Fact]
    public void Defines_the_four_states_from_the_spec()
    {
        Assert.Equal(
            new[] { "Normal", "Maximized", "Minimized", "Borderless" },
            Enum.GetNames<WindowState>());
    }
}
