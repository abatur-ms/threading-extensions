﻿using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Threading.Extensions
{
    /// <summary>
    /// When does the worker stops:
    /// 1. The Actor.Dispose() is called, The worker has 0 consumers AND has no more messages to consume AND worker is blocked on WaitRead()
    ///    in this case we need to make sure that no more handles are being created
    /// 2. The 
    /// </summary>
    internal class Actor : IActor
    {
        private readonly ActorPool _owner;
        private readonly SemaphoreSlim _semaphoreSlim = new SemaphoreSlim(0);
        private readonly ConcurrentQueue<Func<Task>> _workQueue = new ConcurrentQueue<Func<Task>>();
        private Task? _task;

        public Actor(ActorPool owner, string key)
        {
            _owner = owner;
            Key = key;
        }
        
        public string Key { get; }

        public void Enqueue(Func<Task> work)
        {
            lock (this)
            {
                _workQueue.Enqueue(work);
                _semaphoreSlim.Release();
            }
        }

        public void Start()
        {
            _task = RunAsync();
        }

        public bool IsCompleted => _task!.IsCompleted;

        public int Consumers { get; set; }

        private async Task RunAsync()
        {
            while (true)
            {
                await _semaphoreSlim.WaitAsync();

                lock (this)
                {
                    if (_workQueue.Count == 0 && Consumers == 0)
                    {
                        _owner.Return(this);
                        return;
                    }
                }

                if (!_workQueue.TryDequeue(out var work))
                {
                    continue;
                }

                try
                {
                    await work();
                }
                catch
                {
                    // ignored
                }
            }
        }

        internal void DecreaseHandle()
        {
            lock (this)
            {
                Consumers--;
                _semaphoreSlim.Release();
            }
        }

        public void Dispose()
        {
            DecreaseHandle();
        }
    }
}
