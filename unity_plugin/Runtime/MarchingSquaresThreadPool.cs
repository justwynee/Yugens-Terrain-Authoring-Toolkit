// MarchingSquaresThreadPool.cs
// Unity C# port of marching_squares_thread_pool.gd (Godot 4)
// All computational logic is preserved exactly – no differences.

using System;
using System.Collections.Generic;
using System.Threading;

namespace MarchingSquaresTerrain
{
    /// <summary>
    /// Simple parallel job runner.
    ///
    /// Direct port of marching_squares_thread_pool.gd:
    ///   - enqueue(job)  →  Enqueue(job)
    ///   - start()       →  Execute() (combined start + wait)
    ///   - wait()        →  internal barrier inside Execute()
    ///
    /// Uses the CLR thread-pool (via <see cref="ThreadPool"/>) which maps to
    /// Godot's WorkerThreadPool.
    /// </summary>
    public class MarchingSquaresThreadPool
    {
        private readonly int         _maxThreads;
        private readonly List<Action> _jobQueue = new List<Action>();

        /// <param name="maxThreads">Maximum concurrent worker threads.</param>
        public MarchingSquaresThreadPool(int maxThreads = 4)
        {
            _maxThreads = maxThreads;
        }

        /// <param name="jobs">Pre-built list of jobs (alternative to multiple Enqueue calls).</param>
        public MarchingSquaresThreadPool(List<Action> jobs, int maxThreads = 4)
        {
            _maxThreads = maxThreads;
            _jobQueue.AddRange(jobs);
        }

        /// <summary>Adds a job to the queue (mirrors enqueue()).</summary>
        public void Enqueue(Action job)
        {
            _jobQueue.Add(job);
        }

        /// <summary>
        /// Dispatches all queued jobs in parallel (up to maxThreads at a time)
        /// and blocks until every job has completed.
        /// Mirrors start() + wait() in GDScript.
        /// </summary>
        public void Execute()
        {
            if (_jobQueue.Count == 0) return;

            using var semaphore   = new SemaphoreSlim(_maxThreads, _maxThreads);
            using var countDown   = new CountdownEvent(_jobQueue.Count);

            foreach (var job in _jobQueue)
            {
                semaphore.Wait();

                var capturedJob = job;
                ThreadPool.QueueUserWorkItem(_ =>
                {
                    try   { capturedJob(); }
                    finally
                    {
                        semaphore.Release();
                        countDown.Signal();
                    }
                });
            }

            countDown.Wait();
        }
    }
}
