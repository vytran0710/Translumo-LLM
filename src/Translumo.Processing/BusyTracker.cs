using System;
using System.Threading;

namespace Translumo.Processing
{
    /// <summary>Snapshot of translation work: item <see cref="Current"/> of <see cref="Total"/> while busy.</summary>
    public readonly record struct TranslationProgress(bool IsBusy, int Current, int Total)
    {
        public static readonly TranslationProgress Idle = new TranslationProgress(false, 0, 0);
    }

    /// <summary>
    /// Tracks queued work items (one per piece of new text, from detection until its translation is shown or dropped)
    /// and reports progress as "current of total". Counters reset once all items are done.
    /// Notifications are issued under a lock so they are delivered in order.
    /// </summary>
    public sealed class BusyTracker
    {
        public TranslationProgress Progress
        {
            get
            {
                lock (_lock)
                {
                    return Snapshot();
                }
            }
        }

        private readonly object _lock = new object();
        private readonly Action<TranslationProgress> _onProgressChanged;
        private int _active;
        private int _completed;
        private int _total;

        public BusyTracker(Action<TranslationProgress> onProgressChanged)
        {
            _onProgressChanged = onProgressChanged ?? throw new ArgumentNullException(nameof(onProgressChanged));
        }

        /// <summary>Starts a work item; dispose the returned handle when the item is finished or dropped.</summary>
        public IDisposable BeginItem()
        {
            lock (_lock)
            {
                if (_active == 0)
                {
                    _completed = 0;
                    _total = 0;
                }

                _active++;
                _total++;
                _onProgressChanged(Snapshot());
            }

            return new WorkItem(this);
        }

        private void CompleteItem()
        {
            lock (_lock)
            {
                _active--;
                _completed++;
                _onProgressChanged(Snapshot());
            }
        }

        private TranslationProgress Snapshot() =>
            _active == 0 ? TranslationProgress.Idle : new TranslationProgress(true, _completed + 1, _total);

        private sealed class WorkItem : IDisposable
        {
            private readonly BusyTracker _tracker;
            private int _disposed;

            public WorkItem(BusyTracker tracker)
            {
                _tracker = tracker;
            }

            public void Dispose()
            {
                // Completing twice would corrupt the counters
                if (Interlocked.Exchange(ref _disposed, 1) == 0)
                {
                    _tracker.CompleteItem();
                }
            }
        }
    }
}
