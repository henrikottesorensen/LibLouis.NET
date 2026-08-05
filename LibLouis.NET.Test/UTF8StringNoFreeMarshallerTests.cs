using System;
using System.Runtime.InteropServices;
using System.Text;

using Xunit;

namespace LibLouis.NET.Test;

/// <summary>
/// The marshaller used for strings liblouis owns. It only converts inbound: not freeing is
/// correct for memory liblouis allocated, and would be a leak for buffers we allocate ourselves,
/// so it is restricted to return values.
/// </summary>
public unsafe class UTF8StringNoFreeMarshallerTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("nota-tables/da-dk-g26.ctb")]
    [InlineData("Første linje")]      // multi-byte UTF-8
    [InlineData("\U0001D11E")]        // non-BMP, surrogate pair on the managed side
    public void ConvertToManaged_ReadsNulTerminatedUtf8(string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value + "\0");

        fixed (byte* unmanaged = utf8)
        {
            Assert.Equal(value, UTF8StringNoFreeMarshaller.ConvertToManaged(unmanaged));
        }
    }

    /// <summary>
    /// The string must stop at the terminator, not run on into whatever follows it.
    /// </summary>
    [Fact]
    public void ConvertToManaged_StopsAtTheTerminator()
    {
        byte[] utf8 = Encoding.UTF8.GetBytes("abc\0trailing garbage");

        fixed (byte* unmanaged = utf8)
        {
            Assert.Equal("abc", UTF8StringNoFreeMarshaller.ConvertToManaged(unmanaged));
        }
    }

    [Fact]
    public void ConvertToManaged_MapsNullPointerToNull()
    {
        Assert.Null(UTF8StringNoFreeMarshaller.ConvertToManaged(null));
    }

    /// <summary>
    /// Free must leave the memory alone. If it released it, the allocator would abort on the
    /// second release here.
    /// </summary>
    [Fact]
    public void Free_DoesNotReleaseTheMemory()
    {
        byte* buffer = (byte*)NativeMemory.Alloc(4);

        UTF8StringNoFreeMarshaller.Free(buffer);

        // Ours to release, and still ours after Free.
        NativeMemory.Free(buffer);
    }
}
