using System;
using System.IO;
using System.Linq;
using System.Reflection;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// Disposing frees liblouis's translation and display table chains, which are process-global. The
/// wrapper therefore has to serialise lou_free against every other native call, and must refuse
/// to be used afterwards rather than quietly recompiling the tables it just threw away.
/// </summary>
/// <remarks>
/// These tests set the disposed flag directly instead of calling Dispose. LibLouis is a
/// process-wide singleton and the suite runs serially in one process, so really disposing it
/// would fail every test that ran afterwards. The flag is restored in a finally for the same
/// reason. End-to-end disposal is exercised out of process.
/// </remarks>
public class DisposalTests
{
    private static readonly string[] Tables = ["da-dk-braillo.dis", "da-dk-g26.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tables", t))];

    private static FieldInfo DisposedField =>
        typeof(LibLouis).GetField("disposedValue", BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new InvalidOperationException("disposedValue field not found");

    [Fact]
    public void UsingTheInstanceAfterDisposeThrows()
    {
        WhileMarkedDisposed(() =>
        {
            Assert.Throws<ObjectDisposedException>(
                () => LibLouis.Instance.Translate(TablePaths(), "abc", 16, null, null, TranslationMode.Regular));

            Assert.Throws<ObjectDisposedException>(
                () => LibLouis.Instance.Translate(
                    TablePaths(), "abc", 16, null, null, new int[16], new int[16], 0, TranslationMode.Regular));

            Assert.Throws<ObjectDisposedException>(
                () => LibLouis.Instance.BackTranslate(TablePaths(), "abc", 16, null, null, TranslationMode.Regular));

            Assert.Throws<ObjectDisposedException>(
                () => LibLouis.Instance.CharactersToDots(TablePaths(), "abc"));

            Assert.Throws<ObjectDisposedException>(
                () => LibLouis.Instance.DotsToCharacters(TablePaths(), "abc"));

            Assert.Throws<ObjectDisposedException>(
                () => LibLouis.Instance.Hyphenate(TablePaths(), "bogstaver", TranslationMode.Regular));

            Assert.Throws<ObjectDisposedException>(
                () => LibLouis.Instance.IndexTables(TablePaths()));

            Assert.Throws<ObjectDisposedException>(
                () => LibLouis.Instance.FindTable("type:literary"));
        });
    }

    /// <summary>
    /// The instance is usable again once the flag is cleared, so the guard is the only thing
    /// stopping it - the test is not just observing a broken singleton.
    /// </summary>
    [Fact]
    public void TheGuardIsWhatBlocksUse()
    {
        WhileMarkedDisposed(() =>
            Assert.Throws<ObjectDisposedException>(
                () => LibLouis.Instance.CharactersToDots(TablePaths(), "abc")));

        Assert.Equal(3, LibLouis.Instance.CharactersToDots(TablePaths(), "abc").Length);
    }

    /// <summary>
    /// lou_free is process-global, but a finalizer is per managed instance. In a collectible
    /// AssemblyLoadContext that would free the tables of every other context still using them,
    /// and it would do it on the finalizer thread, outside the lock.
    /// </summary>
    [Fact]
    public void LibLouisHasNoFinalizer()
    {
        MethodInfo? finalizer = typeof(LibLouis)
            .GetMethod("Finalize", BindingFlags.NonPublic | BindingFlags.Instance);

        Assert.Equal(typeof(object), finalizer?.DeclaringType);
    }

    [Fact]
    public void DisposedFlagIsVolatile()
    {
        // Read outside the lock by the guards, written under it by Dispose.
        Assert.Contains(
            typeof(LibLouis).GetField("disposedValue", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetRequiredCustomModifiers(),
            m => m == typeof(System.Runtime.CompilerServices.IsVolatile));
    }

    private static void WhileMarkedDisposed(Action body)
    {
        FieldInfo field = DisposedField;

        field.SetValue(LibLouis.Instance, true);

        try
        {
            body();
        }
        finally
        {
            field.SetValue(LibLouis.Instance, false);
        }
    }
}
