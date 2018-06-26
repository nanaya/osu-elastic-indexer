// Copyright (c) 2007-2018 ppy Pty Ltd <contact@ppy.sh>.
// Licensed under the MIT Licence - https://raw.githubusercontent.com/ppy/osu-elastic-indexer/master/LICENCE

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Elasticsearch.Net;
using Nest;

namespace osu.ElasticIndexer
{
    class BulkIndexingDispatcher<T> where T : Model
    {
        // ElasticClient is thread-safe and should be shared per host.
        private static readonly ElasticClient elasticClient = new ElasticClient
        (
            new ConnectionSettings(new Uri(AppSettings.ElasticsearchHost))
        );

        private readonly ConcurrentBag<Task<IBulkResponse>> pendingTasks = new ConcurrentBag<Task<IBulkResponse>>();

        // BlockingCollection queues serve as a self-limiting read-ahead buffer to ensure
        // there is always data ready to be dispatched to Elasticsearch. The separate queue buffer for
        // retries allows retries to preempt the read buffer.
        private readonly BlockingCollection<List<T>> readBuffer = new BlockingCollection<List<T>>(AppSettings.QueueSize);
        private readonly BlockingCollection<List<T>> retryBuffer = new BlockingCollection<List<T>>(AppSettings.QueueSize);
        private readonly BlockingCollection<List<T>>[] queues;

        // throttle control; Gracefully handles busy signals from the server
        // by gruadually increasing the delay on busy signals,
        // while decreasing  as dispatches complete.
        private int delay;

        private string alias;
        private string index;

        internal BulkIndexingDispatcher(string alias, string index)
        {
            this.alias = alias;
            this.index = index;
            queues = new [] { retryBuffer, readBuffer };
        }

        internal void Enqueue(List<T> list) => readBuffer.Add(list);
        internal void EnqueueEnd() => readBuffer.CompleteAdding();

        /// <summary>
        /// Creates a task that loops and takes items from readBuffer until completion,
        /// dispatching them to Elasticsearch for indexing.
        /// <returns>The dispatcher task.</returns>
        // internal Task Start()
        internal void Run()
        {
            // custom partitioner and options to prevent Parallel.ForEach from going out of control.
            var partitioner = Partitioner.Create(
                readBuffer.GetConsumingEnumerable(),
                EnumerablePartitionerOptions.NoBuffering // buffering causes spastic behaviour.
            );

            var options = new ParallelOptions { MaxDegreeOfParallelism = 4 };

            Parallel.ForEach(partitioner, options, (chunk) =>
            {
                throttledWait();
                dispatch(chunk, index).Wait();

                // TODO: Less blind-fire update.
                // I feel like this is in the wrong place...
                IndexMeta.Update(new IndexMeta
                {
                    Index = index,
                    Alias = alias,
                    LastId = chunk.Last().CursorValue,
                    UpdatedAt = DateTimeOffset.UtcNow
                });

                unthrottle();
            });
        }

        private async Task<DynamicResponse> dispatch(List<T> chunk, string index)
        {
            var bulkDescriptor = new BulkDescriptor().Index(index).IndexMany(chunk);
            var response = await elasticClient.BulkAsync(bulkDescriptor);

            // Elasticsearch bulk thread pool is full.
            if (response.ItemsWithErrors.Any(item => item.Status == 429 || item.Error.Type == "es_rejected_execution_exception"))
            {
                Interlocked.Increment(ref delay);

                Console.WriteLine($"Server returned 429, requeued chunk with lastId {chunk.Last().CursorValue}");
                // TODO: only retry items with errors?
                return await dispatch(chunk, index);
            }

            // Index was closed, possibly because it was switched. Flag for bailout.
            if (response.ItemsWithErrors.Any(item => item.Error.Type == "index_closed_exception"))
            {
                Console.Error.WriteLine($"{index} was closed.");
                readBuffer.CompleteAdding();
            }

            // TODO: other errors should do some kind of notification.

            return new DynamicResponse();
        }

        /// <summary>
        /// Slows down when the server is busy or too many requests are in-flight.
        /// </summary>
        private void throttledWait()
        {
            if (delay > 0) Task.Delay(delay * 100).Wait();

            // too many pending responses, wait and let them be handled.
            if (pendingTasks.Count > AppSettings.QueueSize * 2) {
                Console.WriteLine($"Too many pending responses ({pendingTasks.Count}), waiting...");
                pendingTasks.FirstOrDefault()?.Wait();
            }
        }

        /// <summary>
        /// Gradually reduces the delay time between requests.
        /// </summary>
        private void unthrottle()
        {
            if (delay > 0) Interlocked.Decrement(ref delay);
        }
    }
}
