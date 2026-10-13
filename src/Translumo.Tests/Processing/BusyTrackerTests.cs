using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Translumo.Processing;
using Xunit;

namespace Translumo.Tests.Processing
{
    public class BusyTrackerTests
    {
        [Fact]
        public void Single_item_reports_one_of_one_then_idle()
        {
            var changes = new List<TranslationProgress>();
            var tracker = new BusyTracker(changes.Add);

            var item = tracker.BeginItem();
            Assert.Equal(new TranslationProgress(true, 1, 1), tracker.Progress);
            item.Dispose();

            Assert.Equal(new[] { new TranslationProgress(true, 1, 1), TranslationProgress.Idle }, changes);
        }

        [Fact]
        public void Queued_items_report_current_of_total()
        {
            var changes = new List<TranslationProgress>();
            var tracker = new BusyTracker(changes.Add);

            var items = Enumerable.Range(0, 3).Select(_ => tracker.BeginItem()).ToArray();
            Assert.Equal(new TranslationProgress(true, 1, 3), tracker.Progress);

            items[0].Dispose();
            Assert.Equal(new TranslationProgress(true, 2, 3), tracker.Progress);

            var late = tracker.BeginItem(); // new text while still busy extends the same queue
            Assert.Equal(new TranslationProgress(true, 2, 4), tracker.Progress);

            items[1].Dispose();
            items[2].Dispose();
            Assert.Equal(new TranslationProgress(true, 4, 4), tracker.Progress);

            late.Dispose();
            Assert.Equal(TranslationProgress.Idle, tracker.Progress);
            Assert.Equal(TranslationProgress.Idle, changes[^1]);
        }

        [Fact]
        public void Counters_reset_after_idle()
        {
            var tracker = new BusyTracker(_ => { });
            tracker.BeginItem().Dispose();
            tracker.BeginItem().Dispose();

            using var item = tracker.BeginItem();

            Assert.Equal(new TranslationProgress(true, 1, 1), tracker.Progress);
        }

        [Fact]
        public void Disposing_an_item_twice_completes_it_once()
        {
            var tracker = new BusyTracker(_ => { });
            var first = tracker.BeginItem();
            using var second = tracker.BeginItem();

            first.Dispose();
            first.Dispose();

            Assert.Equal(new TranslationProgress(true, 2, 2), tracker.Progress);
        }

        [Fact]
        public void Concurrent_items_end_idle_with_consistent_notifications()
        {
            var changes = new ConcurrentQueue<TranslationProgress>();
            var tracker = new BusyTracker(changes.Enqueue);

            Parallel.For(0, 1000, _ => tracker.BeginItem().Dispose());

            var sequence = changes.ToArray();
            Assert.Equal(TranslationProgress.Idle, tracker.Progress);
            Assert.Equal(TranslationProgress.Idle, sequence[^1]);
            Assert.All(sequence.Where(p => p.IsBusy), p => Assert.InRange(p.Current, 1, p.Total));
        }

        [Fact]
        public void Null_callback_is_rejected()
        {
            Assert.Throws<ArgumentNullException>(() => new BusyTracker(null!));
        }
    }
}
