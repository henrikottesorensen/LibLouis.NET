using System;
using System.IO;
using System.Linq;
using System.Reflection;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// Shutdown frees liblouis's translation and display table chains, which are global to the
/// process. It is deliberately not IDisposable: nothing here is owned by a single caller, so
/// there is no "done with it" moment to hang disposal off, and an accidental
/// <c>using (LibLouis.Instance)</c> would tear liblouis down for everything else in the process.
/// </summary>
/// <remarks>
/// These tests set the flag directly instead of calling Shutdown. LibLouis is a process-wide
/// singleton and the suite runs serially in one process, so really shutting it down would fail
/// every test that ran afterwards. The flag is restored in a finally for the same reason.
/// End-to-end shutdown is exercised out of process.
/// </remarks>
public class ShutdownTests
{
    private static readonly string[] Tables = ["da-dk-braillo.dis", "da-dk-g26.ctb"];

    private static string[] TablePaths() =>
        [.. Tables.Select(t => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "nota-tables", t))];

    private static FieldInfo ShutDownField =>
        typeof(LibLouis).GetField("_shutDown", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("_shutDown field not found");

    /// <summary>
    /// The shape change itself: an accidental using statement must not compile.
    /// </summary>
    [Fact]
    public void LibLouisIsNotDisposable()
    {
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(LibLouis)));
    }

    /// <summary>
    /// Shutdown is process-wide teardown, so it belongs on the type, not on an instance nobody
    /// exclusively owns.
    /// </summary>
    [Fact]
    public void ShutdownIsStatic()
    {
        MethodInfo? shutdown = typeof(LibLouis).GetMethod(
            "Shutdown", BindingFlags.Public | BindingFlags.Static, Type.EmptyTypes);

        Assert.NotNull(shutdown);
        Assert.Equal(typeof(void), shutdown.ReturnType);
    }

    [Fact]
    public void UsingTheInstanceAfterShutdownThrows()
    {
        WhileMarkedShutDown(() =>
        {
            Assert.Throws<InvalidOperationException>(
                () => LibLouis.Instance.Translate(TablePaths(), "abc", 16, null, null, TranslationMode.Regular));

            Assert.Throws<InvalidOperationException>(
                () => LibLouis.Instance.Translate(
                    TablePaths(), "abc", 16, null, null, new int[16], new int[16], 0, TranslationMode.Regular));

            Assert.Throws<InvalidOperationException>(
                () => LibLouis.Instance.BackTranslate(TablePaths(), "abc", 16, null, null, TranslationMode.Regular));

            Assert.Throws<InvalidOperationException>(
                () => LibLouis.Instance.CharactersToDots(TablePaths(), "abc"));

            Assert.Throws<InvalidOperationException>(
                () => LibLouis.Instance.DotsToCharacters(TablePaths(), "abc"));

            Assert.Throws<InvalidOperationException>(
                () => LibLouis.Instance.Hyphenate(TablePaths(), "bogstaver", TranslationMode.Regular));

            Assert.Throws<InvalidOperationException>(
                () => LibLouis.Instance.IndexTables(TablePaths()));

            Assert.Throws<InvalidOperationException>(
                () => LibLouis.Instance.FindTable("type:literary"));
        });
    }

    /// <summary>
    /// The message has to say what happened: "cannot access a disposed object" would be a lie for
    /// a type that is not disposable.
    /// </summary>
    [Fact]
    public void TheFailureExplainsItself()
    {
        WhileMarkedShutDown(() =>
        {
            InvalidOperationException e = Assert.Throws<InvalidOperationException>(
                () => LibLouis.Instance.CharactersToDots(TablePaths(), "abc"));

            Assert.Contains("shut down", e.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Diagnostics stay available: neither touches anything lou_free released.
    /// </summary>
    [Fact]
    public void VersionAndLoggerSurviveShutdown()
    {
        WhileMarkedShutDown(() =>
        {
            Assert.False(string.IsNullOrWhiteSpace(LibLouis.Instance.Version));

            LibLouis.Instance.Logger = new CollectingLogger();
        });
    }

    /// <summary>
    /// The instance works again once the flag is cleared, so the guard is the only thing stopping
    /// it - the test is not just observing a broken singleton.
    /// </summary>
    [Fact]
    public void TheGuardIsWhatBlocksUse()
    {
        WhileMarkedShutDown(() =>
            Assert.Throws<InvalidOperationException>(
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
    public void ShutDownFlagIsVolatile()
    {
        // Read outside the lock by the guards, written under it by Shutdown.
        Assert.Contains(
            ShutDownField.GetRequiredCustomModifiers(),
            m => m == typeof(System.Runtime.CompilerServices.IsVolatile));
    }

    private static void WhileMarkedShutDown(Action body)
    {
        FieldInfo field = ShutDownField;

        field.SetValue(null, true);

        try
        {
            body();
        }
        finally
        {
            field.SetValue(null, false);
        }
    }
}
