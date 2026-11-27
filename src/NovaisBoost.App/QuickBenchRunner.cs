using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NovaisBoost.Core;

namespace NovaisBoost.App;

/// <summary>23.6: about 30 seconds of CPU, memory and disk measurements. Read-only except for one temporary file.</summary>
internal static class QuickBenchRunner
{
    private const FileOptions NoBuffering = (FileOptions)0x20000000; // FILE_FLAG_NO_BUFFERING: read from the disk, not the cache

    public static QuickBenchResult Run(string temporaryFolder)
    {
        var single = Cpu(1, TimeSpan.FromSeconds(4));
        var multi = Cpu(Environment.ProcessorCount, TimeSpan.FromSeconds(4));
        var memory = Memory(TimeSpan.FromSeconds(4));
        var disk = Disk(temporaryFolder, 1L << 30);
        return new QuickBenchResult(single, multi, memory, disk);
    }

    /// <summary>SHA-256 throughput in MB/s with this many threads.</summary>
    private static double Cpu(int threads, TimeSpan duration)
    {
        long bytes = 0;
        var deadline = Stopwatch.StartNew();
        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
        {
            var buffer = new byte[1 << 20];
            Random.Shared.NextBytes(buffer);
            Span<byte> hash = stackalloc byte[32];
            while (deadline.Elapsed < duration)
            {
                SHA256.HashData(buffer, hash);
                Interlocked.Add(ref bytes, buffer.Length);
            }
        });
        return bytes / 1_000_000d / deadline.Elapsed.TotalSeconds;
    }

    /// <summary>Copy bandwidth (read + write) of a 512 MB buffer, in MB/s.</summary>
    private static unsafe double Memory(TimeSpan duration)
    {
        const long size = 512L << 20;
        var source = NativeMemory.AlignedAlloc((nuint)size, 4096);
        var target = NativeMemory.AlignedAlloc((nuint)size, 4096);
        try
        {
            NativeMemory.Fill(source, (nuint)size, 1);
            NativeMemory.Fill(target, (nuint)size, 0);
            long copied = 0;
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < duration)
            {
                Buffer.MemoryCopy(source, target, size, size);
                copied += size * 2;
            }
            return copied / 1_000_000d / watch.Elapsed.TotalSeconds;
        }
        finally
        {
            NativeMemory.AlignedFree(source);
            NativeMemory.AlignedFree(target);
        }
    }

    /// <summary>Writes a temporary file, then reads it back bypassing the file cache. The file is always deleted.</summary>
    private static double Disk(string folder, long size)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"quickbench-{Guid.NewGuid():N}.tmp");
        const int block = 4 << 20;
        try
        {
            var data = GC.AllocateUninitializedArray<byte>(block, pinned: true);
            Random.Shared.NextBytes(data);
            using (var write = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 0, FileOptions.WriteThrough))
                for (long written = 0; written < size; written += block)
                    write.Write(data);

            var watch = Stopwatch.StartNew();
            long read = 0;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, 0, NoBuffering | FileOptions.SequentialScan))
            {
                int count;
                while ((count = stream.Read(data, 0, block)) > 0)
                    read += count;
            }
            return read / 1_000_000d / watch.Elapsed.TotalSeconds;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
