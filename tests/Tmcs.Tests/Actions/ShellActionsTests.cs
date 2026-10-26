using System.Runtime.InteropServices;
using Tmcs.Actions;

namespace Tmcs.Tests.Actions;

/// <summary>
/// <see cref="ShellActions.CopyText"/> through its clipboard seam: the owner's real clipboard is shared state outside the test, so no
/// test writes it. OpenFileLocation and ShowProperties open Explorer and a Properties window, so no test calls them.
/// </summary>
public class ShellActionsTests
{
    [Fact]
    public void Copy_text_sets_the_clipboard()
    {
        var original = ShellActions.ClipboardWriter;
        try
        {
            var received = new List<string>();
            ShellActions.ClipboardWriter = received.Add;

            ShellActions.CopyText("x");

            Assert.Equal(["x"], received);
        }
        finally
        {
            ShellActions.ClipboardWriter = original;
        }
    }

    [Fact]
    public void A_locked_clipboard_is_ignored()
    {
        var original = ShellActions.ClipboardWriter;
        try
        {
            var attempts = 0;
            ShellActions.ClipboardWriter = _ =>
            {
                attempts++;
                throw new ExternalException("Requested Clipboard operation did not succeed.", unchecked((int)0x800401D0)); // CLIPBRD_E_CANT_OPEN
            };

            ShellActions.CopyText("x"); // does not throw

            Assert.Equal(1, attempts);
        }
        finally
        {
            ShellActions.ClipboardWriter = original;
        }
    }
}
