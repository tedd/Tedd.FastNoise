using System;
using System.Threading.Tasks;

namespace Tedd.FastNoise.Internal;

/// <summary>
/// Splits a fill into row ranges and runs them, on one thread or all of them.
/// </summary>
/// <remarks>
/// <para>
/// Rows are the unit of work because a row is contiguous in the destination. Handing each worker a
/// run of whole rows means no two workers ever touch the same cache line, so there is no false
/// sharing to tune away.
/// </para>
/// <para>
/// The destination is pinned for the duration of the parallel loop and handed to workers as a
/// span rebuilt from the pinned address. A lambda cannot close over a <c>Span&lt;T&gt;</c> or a
/// pointer, and the buffer must not move under threads that are writing into it -- pinning inside
/// the blocking <c>Parallel.For</c> satisfies both.
/// </para>
/// </remarks>
internal static class RowScheduler
{
    /// <summary>Fills a contiguous run of rows.</summary>
    /// <param name="destination">The whole destination buffer. Row indices are absolute within it.</param>
    /// <param name="firstRow">First row this call is responsible for.</param>
    /// <param name="rowCount">How many rows to fill.</param>
    internal delegate void RowRange(Span<float> destination, int firstRow, int rowCount);

    /// <summary>Runs every row on the calling thread.</summary>
    public static void Sequential(Span<float> destination, int totalRows, RowRange body)
        => body(destination, 0, totalRows);

    /// <summary>Splits the rows across the thread pool and blocks until all are done.</summary>
    public static unsafe void Parallel(Span<float> destination, int totalRows, RowRange body)
    {
        int workers = Math.Min(Environment.ProcessorCount, totalRows);

        if (workers <= 1 || destination.IsEmpty)
        {
            body(destination, 0, totalRows);
            return;
        }

        int rowsPerWorker = (totalRows + workers - 1) / workers;
        int length = destination.Length;

        fixed (float* origin = destination)
        {
            nint address = (nint)origin;

            System.Threading.Tasks.Parallel.For(0, workers, worker =>
            {
                int firstRow = worker * rowsPerWorker;
                if (firstRow >= totalRows)
                {
                    return;
                }

                int rowCount = Math.Min(rowsPerWorker, totalRows - firstRow);
                body(new Span<float>((float*)address, length), firstRow, rowCount);
            });
        }
    }

    /// <summary>Runs the rows sequentially or in parallel, as asked.</summary>
    public static void Run(Span<float> destination, int totalRows, bool parallel, RowRange body)
    {
        if (parallel)
        {
            Parallel(destination, totalRows, body);
        }
        else
        {
            Sequential(destination, totalRows, body);
        }
    }
}
